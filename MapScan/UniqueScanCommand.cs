namespace Poe2Map;

/// <summary>
///     MapScan uniquescan [regex]: canli alandaki butun yer unique'leri, overlay'in onlara verdigi adla (GroundUniqueReader,
///     overlay'in data\uniques.json'i ile), liste (uyanik / uyuyan), liste icindeki sirasi ve uzaklik. Overlay listeleri 5000
///     kayitta kesiyor (RefreshMonsters); sirasi 5000'i gecen unique overlay'de hic gorunmez. 22.11.2026, kullanici:
///     "covenant a neden yol cizmiyor". Sadece okur.
/// </summary>
internal static class UniqueScanCommand
{
    internal static int Run(string[] args)
    {
        var filter = args.Length > 0 ? new System.Text.RegularExpressions.Regex(args[0], System.Text.RegularExpressions.RegexOptions.IgnoreCase) : null;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            // Overlay'in ad tablosu (MapScan'in kendi data klasorunde yok).
            var overlayJson = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\MapOverlay\bin\Debug\net10.0-windows\data\uniques.json"));
            if (File.Exists(overlayJson) && !File.Exists(UniqueArt.FileName)) { File.Copy(overlayJson, UniqueArt.FileName); }

            if (LiveArea.FindLive(h, 0) is not { } t) { Console.WriteLine("canli alan yok"); return 1; }
            var area = t.StructAddress - GameLayout.TerrainInArea;
            var player = PlayerFinder.ReadAt(h, LiveArea.LastPositionAddress);

            foreach (var (list, offset) in new[] { ("uyanik", EntityReader.AwakeMap), ("uyuyan", EntityReader.SleepingMap) })
            {
                if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }

                var nodes = EntityReader.WalkMap(h, map, 40000);
                Console.WriteLine($"== {list}: liste boyu {map.Size}, gezilen {nodes.Count}");
                var items = 0;
                for (var i = 0; i < nodes.Count; i++)
                {
                    if (EntityReader.ReadInfo(h, nodes[i].Entity) is not { } info || !info.Path.EndsWith("/WorldItem", StringComparison.Ordinal)) { continue; }

                    items++;
                    if (GroundUniqueReader.Classify(h, info) is not { Length: > 0 } name) { continue; }
                    if (filter is not null && !filter.IsMatch(name)) { continue; }

                    var where = info.Components.TryGetValue("Render", out var render) && EntityReader.Read(h, render + EntityReader.RenderPosition, new byte[8]) ? Pos(h, render) : "?";
                    var far = player is { } p && where != "?" ? $"  uzaklik {Distance(h, render, p.X, p.Y):F0}" : "";
                    Console.WriteLine($"   sira {i,5}{(i >= 5000 ? " (OVERLAY SINIRI DISINDA)" : "")}  \"{name}\"  konum {where}{far}  {Grid(h, info)}  [{string.Join(" ", info.Components.Keys)}]");
                }

                Console.WriteLine($"   {items} yer esyasi");
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Positioned bileseninde izgara konumu olabilecek int ciftleri (0 &lt; x,y &lt; 2000): "+off=(x,y)".</summary>
    private static string Grid(IntPtr h, EntityReader.EntityInfo info)
    {
        if (!info.Components.TryGetValue("Positioned", out var positioned)) { return "Positioned yok"; }
        var b = new byte[0x300];
        if (!EntityReader.Read(h, positioned, b)) { return "Positioned okunamadi"; }

        var hits = new List<string>();
        for (var o = 0x10; o + 8 <= b.Length; o += 4)
        {
            var x = BitConverter.ToInt32(b, o);
            var y = BitConverter.ToInt32(b, o + 4);
            if (x is > 10 and < 2000 && y is > 10 and < 2000) { hits.Add($"+{o:X}=({x},{y})"); }
        }

        return "izgara? " + string.Join(" ", hits.Take(6));
    }

    private static string Pos(IntPtr h, ulong render)
    {
        var b = new byte[8];
        return EntityReader.Read(h, render + EntityReader.RenderPosition, b) ? $"({BitConverter.ToSingle(b, 0):F0}, {BitConverter.ToSingle(b, 4):F0})" : "?";
    }

    private static double Distance(IntPtr h, ulong render, float x, float y)
    {
        var b = new byte[8];
        if (!EntityReader.Read(h, render + EntityReader.RenderPosition, b)) { return -1; }
        var dx = BitConverter.ToSingle(b, 0) - x;
        var dy = BitConverter.ToSingle(b, 4) - y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
