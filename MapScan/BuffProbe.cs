namespace Poe2Map;

/// <summary>
///     MapScan buffs [yol-regex]: oyuncunun (ya da eslesen varligin) Buffs bileseninde buff vektorunu arar ve her
///     buff'in kimligini (BuffDefinitions.dat satirinin Id'si) ve ilk 0x60 baytindaki float / int degerleri basar.
///     Sadece okur. Hicbir offset varsayilmiyor: bilesende (bas, son) cifti gibi duran her vektor deneniyor ve
///     elemanlarinda dat satirina (ilk alani Id metni) giden bir isaretci araniyor.
/// </summary>
internal static class BuffProbe
{
    internal static int Run(string[] args)
    {
        var filter = args.Length > 0 ? args[0] : "Metadata/Characters/";
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var area = TerrainFinder.Find(h, _ => { })
                .Select(t => t.StructAddress - GameLayout.TerrainInArea)
                .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
                .OrderByDescending(x => x.Count)
                .FirstOrDefault();

            var target = MinimapIconProbe.Collect(h, area.Area)
                .FirstOrDefault(e => e.Info.Path.StartsWith(filter, StringComparison.OrdinalIgnoreCase) && e.Info.Components.ContainsKey("Buffs"));
            if (target is null || !target.Info.Components.TryGetValue("Buffs", out var buffs)) { Console.WriteLine("varlik / Buffs yok"); return 1; }

            Console.WriteLine($"{target.Info.Path}  Buffs {buffs:X}");
            var comp = new byte[0x200];
            if (!EntityReader.Read(h, buffs, comp)) { return 1; }

            for (var o = 0x10; o + 16 <= comp.Length; o += 8)
            {
                var first = BitConverter.ToUInt64(comp, o);
                var last = BitConverter.ToUInt64(comp, o + 8);
                if (!PlayerChain.LooksLikePointer(first) || last <= first || (last - first) % 8 != 0 || last - first > 8 * 128) { continue; }

                var raw = new byte[(int)(last - first)];
                if (!EntityReader.Read(h, first, raw)) { continue; }

                var lines = new List<string>();
                for (var i = 0; i + 8 <= raw.Length; i += 8)
                {
                    var buff = BitConverter.ToUInt64(raw, i);
                    if (Describe(h, buff) is { } text) { lines.Add(text); }
                }

                if (lines.Count == 0) { continue; }

                Console.WriteLine($"--- vektor Buffs+{o:X}: {raw.Length / 8} eleman");
                foreach (var line in lines) { Console.WriteLine(line); }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>
    ///     MapScan buffs-watch [saniye]: oyuncunun buff vektorunu (Buffs +0x160, 12.11.2026) yarim saniyede bir okur;
    ///     eklenen / kalkan buff'lari ve suren buff'larin degisen sayilarini zamaniyla basar. Kure alinca hangi buff'in
    ///     geldigini ve sayacinin nasil azaldigini gormek icin.
    /// </summary>
    internal static int Watch(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 300;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var area = TerrainFinder.Find(h, _ => { })
                .Select(t => t.StructAddress - GameLayout.TerrainInArea)
                .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
                .OrderByDescending(x => x.Count)
                .FirstOrDefault();

            var player = MinimapIconProbe.Collect(h, area.Area).FirstOrDefault(e => e.Info.Components.ContainsKey("Player"));
            if (player is null || !player.Info.Components.TryGetValue("Buffs", out var buffs)) { Console.WriteLine("oyuncu yok"); return 1; }

            Console.WriteLine($"izleniyor {seconds} sn, Buffs {buffs:X}");
            var before = new Dictionary<ulong, string>();
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                var now = new Dictionary<ulong, string>();
                var vector = new byte[16];
                if (EntityReader.Read(h, buffs + 0x160, vector))
                {
                    var first = BitConverter.ToUInt64(vector, 0);
                    var last = BitConverter.ToUInt64(vector, 8);
                    if (last > first && last - first <= 8 * 256)
                    {
                        var raw = new byte[(int)(last - first)];
                        if (EntityReader.Read(h, first, raw))
                        {
                            for (var i = 0; i + 8 <= raw.Length; i += 8)
                            {
                                var buff = BitConverter.ToUInt64(raw, i);
                                if (Describe(h, buff) is { } text) { now[buff] = text; }
                            }
                        }
                    }
                }

                var stamp = DateTime.Now.ToString("HH:mm:ss.f");
                foreach (var (buff, text) in now)
                {
                    if (!before.TryGetValue(buff, out var old)) { Console.WriteLine($"{stamp} + {text}"); }
                    else if (old != text) { Console.WriteLine($"{stamp} ~ {text}"); }
                }

                foreach (var gone in before.Keys.Where(k => !now.ContainsKey(k))) { Console.WriteLine($"{stamp} - {before[gone]}"); }

                before = now;
                Thread.Sleep(500);
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Buff nesnesinde Id'ye giden satiri bulup kimligi ve sayisal alanlari yazar; bulamazsa null.</summary>
    private static string? Describe(IntPtr h, ulong buff)
    {
        if (!PlayerChain.LooksLikePointer(buff)) { return null; }

        var b = new byte[0x80];
        if (!EntityReader.Read(h, buff, b)) { return null; }

        for (var o = 0; o + 8 <= b.Length; o += 8)
        {
            var row = BitConverter.ToUInt64(b, o);
            if (!MinimapIconProbe.InRange(row)) { continue; }

            var id = MinimapIconProbe.RowId(h, row);
            if (id.Length < 3 || id == "?" || id.Contains('/') || id.Contains(' ')) { continue; }

            var numbers = Enumerable.Range(0, 0x60 / 4)
                .Select(k => (Off: k * 4, F: BitConverter.ToSingle(b, k * 4), I: BitConverter.ToInt32(b, k * 4)))
                .Where(x => x.Off != o && x.Off != o + 4)
                .Select(x => float.IsFinite(x.F) && Math.Abs(x.F) is > 0.001f and < 100000f ? $"+{x.Off:X}={x.F:0.###}f" :
                             x.I is > 0 and < 100000 ? $"+{x.Off:X}={x.I}" : null)
                .Where(s => s is not null);
            return $"  {buff:X}  satir +{o:X}  {id,-40} {string.Join(' ', numbers)}";
        }

        return null;
    }
}
