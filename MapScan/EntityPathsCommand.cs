using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     MapScan paths [regex]: canli alandaki butun varliklari (uyanik + uyuyan) yollarina gore sayar, her yolun
///     bilesen listesini ve oyuncuya en yakin ornegin uzakligini basar. Sadece okur. Yeni bir lig mekaniginin
///     (wisp, isik kuresi, ...) hangi varlik oldugunu bulmak icin ilk adim. Regex verilirse sadece eslesen yollar.
/// </summary>
internal static class EntityPathsCommand
{
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

            var entries = MinimapIconProbe.Collect(h, area.Area);
            var player = entries.FirstOrDefault(e => e.Info.Components.ContainsKey("Player"));
            var me = player is null ? null : Position(h, player.Info);
            Console.WriteLine($"canli alan {area.Area:X}  varlik {entries.Count}  oyuncu {(me is { } p ? $"({p.X:F0}, {p.Y:F0})" : "?")}");

            var groups = entries
                .Where(e => filter is null || filter.IsMatch(e.Info.Path))
                .GroupBy(e => e.Info.Path)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var g in groups)
            {
                var awake = g.Count(e => e.Awake);
                var nearest = me is { } m
                    ? g.Select(e => Position(h, e.Info)).OfType<(float X, float Y)>()
                        .Select(q => MathF.Sqrt(((q.X - m.X) * (q.X - m.X)) + ((q.Y - m.Y) * (q.Y - m.Y))))
                        .DefaultIfEmpty(float.NaN).Min()
                    : float.NaN;
                var name = MapIconReader.DisplayName(h, g.First().Info);
                Console.WriteLine($"{g.Count(),5} (uyanik {awake,4})  en yakin {nearest,7:F0}  {g.Key}{(name.Length > 0 ? $"  \"{name}\"" : "")}");
                Console.WriteLine($"        {string.Join(' ', g.First().Info.Components.Keys)}");
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static (float X, float Y)? Position(IntPtr h, EntityReader.EntityInfo info)
    {
        if (!info.Components.TryGetValue("Render", out var render)) { return null; }

        var buf = new byte[8];
        if (!EntityReader.Read(h, render + EntityReader.RenderPosition, buf)) { return null; }

        var x = BitConverter.ToSingle(buf, 0);
        var y = BitConverter.ToSingle(buf, 4);
        return float.IsFinite(x) && float.IsFinite(y) ? (x, y) : null;
    }
}
