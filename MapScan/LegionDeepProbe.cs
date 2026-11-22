using System.Text;

namespace Poe2Map;

/// <summary>
///     MapScan legion-deep [metin]: odul tasiyan Legion canavarinin (buff "legion_reward") odulunun turu nerede.
///     21.11.2026. Odullu canavarin bilesenlerinden ve odul buff nesnelerinden 4 kademeye kadar isaretci izlenir; icinde
///     verilen metin (varsayilan "Reward") gecen her UTF-16 metne giden yol basilir. Sadece okur.
/// </summary>
internal static class LegionDeepProbe
{
    internal static int Run(string[] args)
    {
        var needle = args.Length > 0 ? args[0] : "Reward";
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

            var legion = MinimapIconProbe.Collect(h, area.Area).Where(e => HiddenMonsters.IsLegion(e.Info) && e.Info.Components.ContainsKey("Buffs")).ToList();
            var shown = 0;
            foreach (var e in legion)
            {
                var rewards = HiddenMonsters.BuffList(h, e.Info.Components["Buffs"]).Where(b => b.Id.StartsWith("legion_reward", StringComparison.Ordinal)).ToList();
                if (rewards.Count == 0) { continue; }

                Console.WriteLine($"== {e.Info.Path}  {e.Entity:X}  {string.Join(",", rewards.Select(r => r.Id))}");
                var found = new List<string>();
                var seen = new HashSet<ulong> { e.Entity };
                foreach (var (buff, id) in rewards) { Walk(h, buff, id, 0, needle, found, seen); }
                foreach (var (name, address) in e.Info.Components) { Walk(h, address, name, 0, needle, found, seen); }

                foreach (var line in found.Distinct().Take(40)) { Console.WriteLine("   " + line); }
                Console.WriteLine($"   ({seen.Count} dugum)");
                if (++shown >= 5) { break; }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static void Walk(IntPtr h, ulong address, string where, int depth, string needle, List<string> found, HashSet<ulong> seen)
    {
        if (!seen.Add(address) || seen.Count > 60000) { return; }

        var buf = new byte[0x100];
        if (!EntityReader.Read(h, address, buf)) { return; }

        for (var o = 0; o + 8 <= buf.Length; o += 8)
        {
            var p = BitConverter.ToUInt64(buf, o);
            if (p is < 0x10000 or > 0x7FFFFFFFFFFF) { continue; }

            if (Text(h, p) is { } text)
            {
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase)) { found.Add($"{where}+{o:X} = \"{text}\""); }
                continue;
            }

            if (depth < 4 && (p & 7) == 0) { Walk(h, p, $"{where}+{o:X}", depth + 1, needle, found, seen); }
        }
    }

    private static string? Text(IntPtr h, ulong p)
    {
        var raw = new byte[160];
        if (!EntityReader.Read(h, p, raw)) { return null; }

        var s = Encoding.Unicode.GetString(raw);
        var end = s.IndexOf('\0');
        if (end < 4) { return null; }

        s = s[..end];
        return s.All(c => c is >= ' ' and < (char)0x7F) ? s : null;
    }
}
