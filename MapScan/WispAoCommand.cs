using System.Text;

namespace Poe2Map;

/// <summary>
///     MapScan wispao [adet]: wisp'in (AzmeriResourceBase) turunu tasiyan "wisp_*_{sml,med,big}" metnini bilesenlerinde
///     arar (iki kademe isaretci). 18.11.2026: Animated +0x260'taki animasyon varligi uzak wisp'lerde okunamiyor (209'un
///     hepsi), yalnizca yakindakiler cizildi. Yakin ve uzak wisp'leri karsilastirip yuklenmeden de okunabilen yolu bulmak
///     icin. Sadece okur.
/// </summary>
internal static class WispAoCommand
{
    internal static int Run(string[] args)
    {
        var count = args.Length > 0 && int.TryParse(args[0], out var c) ? c : 4;
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
                .FirstOrDefault().Area;
            var player = LocalPlayer.Resolve(h, area) is { } pr ? LocalPlayer.ReadPosition(h, pr.Render) : null;

            var wisps = MinimapIconProbe.Collect(h, area)
                .Where(e => e.Info.Path.Contains("AzmeriResource", StringComparison.Ordinal))
                .Select(e =>
                {
                    var pos = e.Info.Components.TryGetValue("Render", out var r) ? LocalPlayer.ReadPosition(h, r) : null;
                    var d = pos is { } q && player is { } p ? Math.Sqrt(((q.X - p.X) * (q.X - p.X)) + ((q.Y - p.Y) * (q.Y - p.Y))) : 1e9;
                    return (Entry: e, Dist: d);
                })
                .OrderBy(x => x.Dist)
                .ToList();

            Console.WriteLine($"{wisps.Count} wisp; en yakin {count / 2} ve en uzak {count - (count / 2)} inceleniyor");
            var picked = wisps.Take(count / 2).Concat(wisps.Skip(Math.Max(count / 2, wisps.Count - (count - (count / 2))))).ToList();

            foreach (var (e, dist) in picked)
            {
                Console.WriteLine($"=== {e.Entity:X} uzaklik {dist:0} {(e.Awake ? "uyanik" : "uyuyan")}");
                if (!e.Info.Components.TryGetValue("Animated", out var animated)) { Console.WriteLine("  Animated yok"); continue; }

                var ao = PlayerChain.ReadPointer(h, animated + WildwoodReader.AnimatedObject);
                var head = new byte[0x90];
                if (EntityReader.Read(h, ao, head))
                {
                    var details = BitConverter.ToUInt64(head, 0x08);
                    var d = new byte[0x30];
                    var path = EntityReader.Read(h, details, d) ? EntityReader.ReadWString(h, d, 0x08) : "?";
                    Console.WriteLine($"  Animated+260 -> {ao:X}  bayrak8C={head[0x8C]:X2} details={details:X} yol=\"{path}\"");
                }
                else { Console.WriteLine($"  Animated+260 -> {ao:X} okunamadi"); }

                foreach (var (name, address) in e.Info.Components)
                {
                    Search(h, $"{name}", address, 0x400, 2, new HashSet<ulong> { e.Entity, address }, IsWispText);
                }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static bool IsWispText(string s) =>
        s.Contains("wisp", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".ao", StringComparison.OrdinalIgnoreCase);

    /// <summary>at'teki size bayt icinde isaretcileri izler; want'i saglayan metne varan zinciri basar.</summary>
    internal static void Search(IntPtr h, string chain, ulong at, int size, int depth, HashSet<ulong> visited, Func<string, bool> want)
    {
        var buf = new byte[size];
        if (!EntityReader.Read(h, at, buf)) { return; }

        for (var o = 0; o + 8 <= size; o += 8)
        {
            var p = BitConverter.ToUInt64(buf, o);
            if (!PlayerChain.LooksLikePointer(p) || !visited.Add(p)) { continue; }

            var link = $"{chain}+{o:X}";
            if (Text(h, p, want) is { } s) { Console.WriteLine($"  {link} -> \"{s}\""); continue; }
            if (depth > 1) { Search(h, link + " ->", p, 0x100, depth - 1, visited, want); }
        }
    }

    /// <summary>Adreste UTF-16 ya da ASCII metin varsa (en az 4 harf) ve want'i sagliyorsa metin, yoksa null.</summary>
    internal static string? Text(IntPtr h, ulong p, Func<string, bool> want)
    {
        var raw = new byte[0x100];
        if (!EntityReader.Read(h, p, raw)) { return null; }

        foreach (var s in new[] { Wide(raw), Ascii(raw) })
        {
            if (s.Length >= 4 && want(s)) { return s; }
        }

        return null;
    }

    private static string Wide(byte[] raw)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 1 < raw.Length; i += 2)
        {
            var ch = (char)BitConverter.ToUInt16(raw, i);
            if (ch == 0 || ch < 0x20 || ch > 0x7E) { break; }
            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string Ascii(byte[] raw)
    {
        var sb = new StringBuilder();
        foreach (var b in raw)
        {
            if (b == 0 || b < 0x20 || b > 0x7E) { break; }
            sb.Append((char)b);
        }

        return sb.ToString();
    }
}
