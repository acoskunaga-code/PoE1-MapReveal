using System.Text;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     MapScan monstermods [regex]: uyanik magic / rare / unique canavarlarin mod listesini (ObjectMagicProperties +0x150,
///     eleman 0x38 bayt) cozer: her elemanin her 8 baytlik yuvasi bir dat satirini gosteriyorsa satirin +0 Id'si ve
///     satirdaki ilk okunabilir metinler basilir. Regex verilirse yalnizca Id / ad / yol eslesen canavarlar. Sadece okur.
///     20.11.2026: "Wasting Well" (Abyssal Wasting laneti birakan Abyss modu) hangi modda, hangi alanda.
/// </summary>
internal static class MonsterModsCommand
{
    private const ulong ModVector = 0x150;
    private const int ModSize = 0x38;
    private const ulong Rarity = 0x144;

    internal static int Run(string[] args)
    {
        var filter = args.Length > 0 ? new Regex(args[0], RegexOptions.IgnoreCase) : null;
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
            if (EntityReader.ReadMapHeader(h, area.Area + EntityReader.AwakeMap) is not { } map) { Console.WriteLine("alan yok"); return 1; }

            var shown = 0;
            foreach (var (_, entity) in EntityReader.WalkMap(h, map, 5000))
            {
                if (EntityReader.ReadInfo(h, entity) is not { IsMonster: true } info) { continue; }
                if (!info.Components.TryGetValue("ObjectMagicProperties", out var omp)) { continue; }

                var buf = new byte[16];
                if (!EntityReader.Read(h, omp + Rarity, buf)) { continue; }
                var rarity = BitConverter.ToInt32(buf, 0);
                if (rarity is < 1 or > 3) { continue; }
                if (!EntityReader.Read(h, omp + ModVector, buf)) { continue; }

                var first = BitConverter.ToUInt64(buf, 0);
                var last = BitConverter.ToUInt64(buf, 8);
                if (last <= first || last - first > ModSize * 64 || (last - first) % ModSize != 0) { continue; }

                var raw = new byte[(int)(last - first)];
                if (!EntityReader.Read(h, first, raw)) { continue; }

                var lines = new List<string>();
                for (var m = 0; m + ModSize <= raw.Length; m += ModSize)
                {
                    var parts = new List<string>();
                    for (var o = 0; o + 8 <= ModSize; o += 8)
                    {
                        if (Describe(h, BitConverter.ToUInt64(raw, m + o)) is { } text) { parts.Add($"+{o:X2} {text}"); }
                    }

                    lines.Add($"    mod {m / ModSize,2}: {string.Join("  |  ", parts)}");
                }

                if (filter is not null && !filter.IsMatch(info.Path) && !lines.Any(filter.IsMatch)) { continue; }

                Console.WriteLine($"== {info.Path}  nadirlik {rarity}  {lines.Count} mod");
                lines.ForEach(Console.WriteLine);
                if (++shown >= 12) { break; }
            }

            Console.WriteLine($"{shown} canavar basildi");
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Isaretci bir dat satirini gosteriyorsa: Id ve satirin ilk 0x80 baytindaki metinler.</summary>
    private static string? Describe(IntPtr h, ulong row)
    {
        if (row is < 0x10000 or > 0x7FFFFFFFFFFF) { return null; }

        var raw = new byte[0x80];
        if (!EntityReader.Read(h, row, raw)) { return null; }

        var texts = new List<string>();
        for (var o = 0; o + 8 <= raw.Length && texts.Count < 4; o += 4)
        {
            if (Text(h, BitConverter.ToUInt64(raw, o)) is { } t) { texts.Add($"[{o:X}]\"{t}\""); }
        }

        return texts.Count == 0 ? null : $"satir {row:X} " + string.Join(" ", texts);
    }

    private static string? Text(IntPtr h, ulong p)
    {
        if (p is < 0x10000 or > 0x7FFFFFFFFFFF) { return null; }

        var raw = new byte[96];
        if (!EntityReader.Read(h, p, raw)) { return null; }

        var s = Encoding.Unicode.GetString(raw);
        var end = s.IndexOf('\0');
        if (end < 3) { return null; }

        s = s[..end];
        return s.All(c => c is >= ' ' and < (char)0x7F) ? s : null;
    }
}
