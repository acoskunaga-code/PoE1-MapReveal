using System.Text;

namespace Poe2Map;

/// <summary>
///     Gecis kapilarinin ADINI bellekten okuyabiliyor muyuz? AreaTransition bilesenli her varligin Render,
///     AreaTransition, MinimapIcon ve Targetable bilesenlerinde okunabilir metin arar:
///
///         - her 8 baytlik yuvada std::wstring (satir ici ya da isaretciyle),
///         - her isaretcinin gosterdigi yerde UTF-16 ya da ASCII metin; ve bir hop daha (WorldAreaInfoPtr gibi
///           bir kayit, adini baska bir isaretciyle tutar).
///
///     DLL ipucu (sadece alan SIRASI): Render'da Name = Pos'tan 0x20 sonra, AreaTransition'da WorldAreaInfoPtr.
///     Burada hicbir offset varsayilmiyor; metin nerede cikarsa orada.
///
///         exitnames
/// </summary>
internal static class ExitNamesProbe
{
    internal static int Run()
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var terrains = TerrainFinder.Find(h);
            if (LocalPlayer.Locate(h, terrains) is not { } live) { Console.WriteLine("canli alan bulunamadi"); return 1; }

            var area = live.Area.StructAddress - GameLayout.TerrainInArea;
            var seen = new HashSet<ulong>();
            foreach (var mapOffset in new[] { EntityReader.AwakeMap, EntityReader.SleepingMap })
            {
                if (EntityReader.ReadMapHeader(h, area + mapOffset) is not { } map) { continue; }

                foreach (var (_, entity) in EntityReader.WalkMap(h, map))
                {
                    if (!seen.Add(entity)) { continue; }
                    if (EntityReader.ReadInfo(h, entity) is not { } info || !info.Components.ContainsKey("AreaTransition")) { continue; }

                    Console.WriteLine($"\n== {info.Path}  (0x{entity:X})");
                    foreach (var name in new[] { "Render", "AreaTransition", "MinimapIcon", "Targetable" })
                    {
                        if (!info.Components.TryGetValue(name, out var component)) { continue; }

                        var found = Harvest(h, component, 0x1C0).ToList();
                        Console.WriteLine($"   {name} (0x{component:X}): " +
                                          (found.Count == 0 ? "(metin yok)" : string.Join("   ", found.Select(f => $"{f.Where} \"{f.Text}\""))));
                    }
                }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>
    ///     Canli alan nesnesinden okunabilir metinler: alan kimligi ("2_8_12_1") ve adi burada olmali (CurrentArea
    ///     isaretcisi -> WorldAreas satiri). Radar'in alan bazli listesine ve alan adina anahtar.
    /// </summary>
    internal static int RunArea()
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var terrains = TerrainFinder.Find(h);
            if (LocalPlayer.Locate(h, terrains) is not { } live) { Console.WriteLine("canli alan bulunamadi"); return 1; }

            var area = live.Area.StructAddress - GameLayout.TerrainInArea;
            Console.WriteLine($"alan nesnesi 0x{area:X}");
            foreach (var (where, text) in Harvest(h, area, 0x1200)) { Console.WriteLine($"  {where}  \"{text}\""); }

            // CurrentArea: alan + 0xB8 -> WorldAreas satiri ("Data/WorldAreas.dat" +0x08'de). Satirin kendi metinleri:
            var record = PlayerChain.ReadPointer(h, area + 0xB8);
            Console.WriteLine($"\nWorldAreas satiri (alan+0xB8) 0x{record:X}:");
            foreach (var (where, text) in Harvest(h, record, 0x300)) { Console.WriteLine($"  {where}  \"{text}\""); }
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Bilesen penceresinde okunabilir metinleri verir: (yer, metin). Iki seviye isaretci izlenir.</summary>
    internal static IEnumerable<(string Where, string Text)> Harvest(IntPtr h, ulong component, int window)
    {
        var raw = new byte[window];
        if (!EntityReader.Read(h, component, raw)) { yield break; }

        var emitted = new HashSet<string>();
        for (var o = 0; o + 0x20 <= window; o += 8)
        {
            var inline = EntityReader.ReadWString(h, raw, o);
            if (Readable(inline) && emitted.Add(inline)) { yield return ($"+0x{o:X} wstr", inline); }

            var p = BitConverter.ToUInt64(raw, o);
            if (!PlayerChain.LooksLikePointer(p)) { continue; }

            var direct = TextAt(h, p);
            if (Readable(direct) && emitted.Add(direct)) { yield return ($"+0x{o:X} ->", direct); }

            // Ikinci seviye: isaretcinin gosterdigi kayit (ornegin WorldAreas satiri) metni baska isaretciyle tutar.
            var record = new byte[0x80];
            if (!EntityReader.Read(h, p, record)) { continue; }

            for (var q = 0; q + 8 <= record.Length; q += 8)
            {
                var pp = BitConverter.ToUInt64(record, q);
                if (!PlayerChain.LooksLikePointer(pp)) { continue; }

                var deep = TextAt(h, pp);
                if (Readable(deep) && emitted.Add(deep)) { yield return ($"+0x{o:X}->+0x{q:X} ->", deep); }
            }
        }
    }

    /// <summary>Adreste UTF-16 ya da ASCII bir metin (sonlandirici null'a kadar, yazdirilabilir).</summary>
    private static string TextAt(IntPtr h, ulong address)
    {
        var buf = new byte[96];
        if (!EntityReader.Read(h, address, buf)) { return ""; }

        var wide = new StringBuilder();
        for (var i = 0; i + 1 < buf.Length; i += 2)
        {
            var c = buf[i] | (buf[i + 1] << 8);
            if (c == 0) { break; }
            if (c < 0x20 || c > 0x7E) { wide.Clear(); break; }
            wide.Append((char)c);
        }

        if (wide.Length >= 3) { return wide.ToString(); }

        var narrow = new StringBuilder();
        foreach (var b in buf)
        {
            if (b == 0) { break; }
            if (b < 0x20 || b > 0x7E) { narrow.Clear(); break; }
            narrow.Append((char)b);
        }

        return narrow.ToString();
    }

    private static bool Readable(string text) =>
        text.Length >= 3 && text.Count(char.IsLetter) >= 3;
}
