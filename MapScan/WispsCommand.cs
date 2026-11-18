namespace Poe2Map;

/// <summary>
///     MapScan wisps [yol-parcasi]: Wildwood varliklarini (varsayilan "Azmeri") tek tek basar: uyanik/uyuyan,
///     konum, oyuncuya uzaklik, Render sahibi mi, Animated +0x260 animasyon yolu ve WildwoodReader'in verdigi tur.
///     Amac: overlay'in cizmedigi wisp'lerin neden atlandigini gormek. Sadece okur.
/// </summary>
internal static class WispsCommand
{
    internal static int Run(string[] args)
    {
        var filter = args.Length > 0 ? args[0] : "Azmeri";
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
            Console.WriteLine($"alan {area:X}  oyuncu {(player is { } pp ? $"({pp.X:0}, {pp.Y:0})" : "?")}");

            var rows = new List<(double Dist, string Line)>();
            foreach (var e in MinimapIconProbe.Collect(h, area))
            {
                if (!e.Info.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) { continue; }

                var owned = e.Info.Components.TryGetValue("Render", out var render) && EntityReader.OwnedBy(h, render, e.Entity);
                var pos = owned ? LocalPlayer.ReadPosition(h, render) : null;
                var dist = pos is { } q && player is { } pl ? Math.Sqrt(((q.X - pl.X) * (q.X - pl.X)) + ((q.Y - pl.Y) * (q.Y - pl.Y))) : -1;

                var ao = "-";
                if (e.Info.Components.TryGetValue("Animated", out var animated))
                {
                    var p = PlayerChain.ReadPointer(h, animated + WildwoodReader.AnimatedObject);
                    ao = EntityReader.ReadInfo(h, p) is { } aoInfo ? Tail(aoInfo.Path) : $"okunamadi {p:X}";
                }

                var marker = WildwoodReader.Classify(h, e.Info);
                var kind = marker is null ? "null(tekrar)" : $"{marker.Kind}/{marker.Size}";
                rows.Add((dist, $"{(e.Awake ? "U" : "u")} {dist,6:0} {(pos is { } z ? $"({z.X:0},{z.Y:0})" : "(?)"),-14} " +
                               $"render={(owned ? "evet" : "HAYIR")} {kind,-14} ao={ao}  {Tail(e.Info.Path)}"));
            }

            foreach (var r in rows.OrderBy(r => r.Dist)) { Console.WriteLine(r.Line); }
            Console.WriteLine($"{rows.Count} varlik");
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static string Tail(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }
}
