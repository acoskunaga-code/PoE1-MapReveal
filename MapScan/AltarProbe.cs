namespace Poe2Map;

/// <summary>
///     MapScan altars: Eldritch altarlarinin (CleansingFireAltar, TangleAltar) iki seceneginin bellekte nerede
///     durdugunu arar. Sadece okur. Her bilesenin ilk 0x200 baytindaki isaretcilerden iki kademe derine inip
///     okunabilir metinleri (mod kimlikleri, "Player gains" gibi yazilar) yollariyla birlikte basar.
/// </summary>
internal static class AltarProbe
{
    private const int ComponentWindow = 0x200;

    internal static int Run(string[] args)
    {
        var filter = args.Length > 0 ? args[0] : "Altar";
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

            foreach (var e in MinimapIconProbe.Collect(h, area.Area).Where(e => e.Info.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"=== {e.Info.Path}  {(e.Awake ? "uyanik" : "uyuyan")}  varlik {e.Entity:X}  \"{MapIconReader.DisplayName(h, e.Info)}\"");
                Console.WriteLine($"    bilesenler: {string.Join(' ', e.Info.Components.Keys)}");
                foreach (var (name, address) in e.Info.Components)
                {
                    var seen = new HashSet<string>();
                    Sweep(h, address, $"{name}", 0, seen, e.Entity);
                }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static void Sweep(IntPtr h, ulong address, string path, int depth, HashSet<string> seen, ulong entity)
    {
        var size = depth == 0 ? ComponentWindow : 0x80;
        var buf = new byte[size];
        if (!EntityReader.Read(h, address, buf)) { return; }

        for (var o = 0; o + 8 <= size; o += 8)
        {
            var p = BitConverter.ToUInt64(buf, o);
            if (!MinimapIconProbe.InRange(p) || p == entity || p == address) { continue; }

            var here = $"{path}+{o:X}";
            var text = MinimapIconProbe.Text(h, p);
            if (text.Length > 0)
            {
                if (seen.Add(here + text)) { Console.WriteLine($"    {here,-34} {text}"); }
                continue;
            }

            if (depth < 2 && (p & 7) == 0) { Sweep(h, p, here + " ->", depth + 1, seen, entity); }
        }
    }
}
