namespace Poe2Map;

/// <summary>
///     Canli alanin BUTUN karo dosyalarini sayisi ve konumuyla listeler; az sayida gorunen (ozel) karolar basta.
///     Cikis karosunun adini bilmedigimiz alanlarda aday aramak icin: cikislar genelde alanda bir kez gecer.
///
///         tilelist [en cok sayi=3]
/// </summary>
internal static class TileListCommand
{
    internal static int Run(string[] args)
    {
        var maxCount = args.Length > 0 ? int.Parse(args[0]) : 3;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var terrains = TerrainFinder.Find(h);
            if (LocalPlayer.Locate(h, terrains) is not { } live) { Console.WriteLine("canli alan bulunamadi"); return 1; }

            var area = live.Area.StructAddress - GameLayout.TerrainInArea;
            var player = LocalPlayer.ReadPosition(h, live.Render);
            var tiles = TileReader.Read(h, area + GameLayout.TerrainInArea,
                live.Area.GridX / TileReader.CellsPerTile, live.Area.GridY / TileReader.CellsPerTile);
            Console.WriteLine($"alan {live.Area.GridX}x{live.Area.GridY}, {tiles.Count} karo, oyuncu " +
                              (player is { } p ? $"({p.X:F0}, {p.Y:F0})" : "?"));

            // Once TGT detay adi olan karolar: tasarimcilarin isaretledigi onemli yerler.
            var withDetail = tiles.Where(t => t.Detail.Length > 0).GroupBy(t => (t.Detail, t.Path)).ToList();
            Console.WriteLine($"detay adi olan karo: {tiles.Count(t => t.Detail.Length > 0)} ({withDetail.Count} farkli ad/yol)\n");
            foreach (var g in withDetail.OrderBy(g => g.Key.Detail))
            {
                var first = g.First();
                var (dx, dy) = TileReader.Center(first);
                var shown = g.Key.Path.Length > 80 ? "..." + g.Key.Path[^77..] : g.Key.Path;
                Console.WriteLine($"  \"{g.Key.Detail}\"  {g.Count(),3}x  ({dx,7:F0},{dy,7:F0})  {shown}");
            }

            Console.WriteLine();
            var groups = tiles.GroupBy(t => t.Path).Select(g => (Path: g.Key, Count: g.Count(), First: g.First())).ToList();
            Console.WriteLine($"{groups.Count} farkli karo dosyasi; en fazla {maxCount} kez gecenler (ozel karolar):\n");
            foreach (var g in groups.Where(g => g.Count <= maxCount).OrderBy(g => g.Count).ThenBy(g => g.Path))
            {
                var (x, y) = TileReader.Center(g.First);
                var distance = player is { } pp ? MathF.Sqrt(((x - pp.X) * (x - pp.X)) + ((y - pp.Y) * (y - pp.Y))) : 0f;
                var shortPath = g.Path.Length > 95 ? "..." + g.Path[^92..] : g.Path;
                Console.WriteLine($"  {g.Count,2}x  ({x,7:F0},{y,7:F0})  uzaklik {distance,6:F0}  {shortPath}");
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
