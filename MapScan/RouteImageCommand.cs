namespace Poe2Map;

/// <summary>
///     MapScan routeimg &lt;dosya.bmp&gt; [cikis adi]: overlay'in yol hesabini canli zeminle yapip resmini cizer.
///     Ince hucreler: siyah = kapali, gri = acik; koyu kirmizi = ince hucre kapali ama kaba izgarada (Factor 2,
///     2x2'nin en acigi) acik sayilan hucre - yolun ince duvardan "sizdigi" yer. Camgobegi = yol, sari = oyuncu,
///     beyaz = hedef, kirmizi/yesil nokta = kapali/acik TriggerableBlockage (+0x30). Kapali hucreden gecen yol noktalari
///     sayilip yaziliyor. 17.11.2026: Hive kulesinde "yine yanlis ciziyor" icin. Sadece okur.
/// </summary>
internal static class RouteImageCommand
{
    private const int Scale = 3;
    private const int Margin = 40;

    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: routeimg <dosya.bmp> [cikis adi]"); return 2; }

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            if (LocalPlayer.Locate(h, TerrainFinder.Find(h, null), null) is not { } live ||
                LocalPlayer.ReadPosition(h, live.Render) is not { } p)
            {
                Console.Error.WriteLine("Canli alan ya da oyuncu bulunamadi.");
                return 1;
            }

            // Istege bagli baslangic: "routeimg dosya ad x y" - oyuncu baska yerdeyken o noktadan.
            if (args.Length >= 4 && float.TryParse(args[2], out var sx) && float.TryParse(args[3], out var sy))
            {
                p = (sx, sy, p.Z);
            }

            var t = live.Area;
            var area = t.StructAddress - GameLayout.TerrainInArea;
            var raw = new byte[t.DataLength];
            if (!EntityReader.Read(h, t.DataStart, raw)) { Console.Error.WriteLine("zemin okunamadi"); return 1; }

            var doors = new List<(float X, float Y)>();
            var entityExits = ExitFinder.Find(h, area, new Dictionary<(uint, ulong), EntityReader.EntityInfo?>(), doors);
            var tiles = TileExits.FromTiles(TileReader.Read(h, area + GameLayout.TerrainInArea,
                t.GridX / TileReader.CellsPerTile, t.GridY / TileReader.CellsPerTile), AreaIdentity.RadarId(h, area), out _);
            var exits = TileExits.WithEntities(tiles, entityExits);
            var want = args.Length >= 2 ? args[1] : null;
            var target = exits.Where(e => want is null || e.Name.Contains(want, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => Math.Pow(e.X - p.X, 2) + Math.Pow(e.Y - p.Y, 2)).FirstOrDefault();
            if (target.Name is null) { Console.Error.WriteLine("hedef cikis yok: " + string.Join(", ", exits.Select(e => e.Name))); return 1; }

            var grid = FlowField.BuildCostGrid(raw, t.BytesPerRow, t.GridX, t.GridY, doors, out var w, out var hh);
            var field = FlowField.Build(grid, w, hh, target.X, target.Y, target.Region);
            var path = field.PathFrom(p.X, p.Y);
            Console.WriteLine($"oyuncu ({p.X:F0}, {p.Y:F0})  hedef {target.Name} ({target.X:F0}, {target.Y:F0})  " +
                              $"{doors.Count} kapi  yol {path.Count} nokta");

            int Cell(int x, int y)
            {
                if (x < 0 || y < 0 || x >= t.GridX || y >= t.GridY) { return 0; }
                var i = ((long)y * t.BytesPerRow) + (x / 2);
                return i >= raw.Length ? 0 : (x & 1) == 0 ? raw[i] & 0x0F : raw[i] >> 4;
            }

            // Yolun gectigi kaba hucrenin 2x2 ince hucresinden kapali olanlar: kaba izgarada acik ama ince duvar.
            var leaks = path.Count(q => Cell((int)q.X, (int)q.Y) == 0 && Cell((int)q.X - 1, (int)q.Y - 1) == 0);
            Console.WriteLine($"ince hucresi kapali yol noktasi: {leaks}");

            var cells = path.Select(q => (X: (int)q.X, Y: (int)q.Y))
                .Append((X: (int)(p.X / PlayerChain.WorldPerCell), Y: (int)(p.Y / PlayerChain.WorldPerCell)))
                .Append((X: (int)(target.X / PlayerChain.WorldPerCell), Y: (int)(target.Y / PlayerChain.WorldPerCell)))
                .ToList();
            var x0 = Math.Max(0, cells.Min(c => c.X) - Margin);
            var y0 = Math.Max(0, cells.Min(c => c.Y) - Margin);
            var x1 = Math.Min(t.GridX - 1, cells.Max(c => c.X) + Margin);
            var y1 = Math.Min(t.GridY - 1, cells.Max(c => c.Y) + Margin);
            var img = new Image((x1 - x0 + 1) * Scale, (y1 - y0 + 1) * Scale);

            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var v = Cell(x, y);
                    var coarse = grid[((y / FlowField.Factor) * w) + (x / FlowField.Factor)];
                    var color = v > 0 ? (40 + (v * 25), 40 + (v * 25), 40 + (v * 25)) : coarse > 0 ? (110, 0, 0) : (0, 0, 0);
                    img.Fill((x - x0) * Scale, (y - y0) * Scale, Scale, color);
                }
            }

            foreach (var q in path) { img.Fill((((int)q.X) - x0) * Scale, (((int)q.Y) - y0) * Scale, Scale, (0, 235, 235)); }

            foreach (var (bx, by, closed) in Blockages(h, area))
            {
                var gx = (int)(bx / PlayerChain.WorldPerCell);
                var gy = (int)(by / PlayerChain.WorldPerCell);
                if (gx < x0 || gy < y0 || gx > x1 || gy > y1) { continue; }
                img.Fill(((gx - x0) * Scale) - 4, ((gy - y0) * Scale) - 4, 9, closed ? (255, 40, 40) : (40, 255, 40));
            }

            img.Fill((((int)(p.X / PlayerChain.WorldPerCell)) - x0) * Scale - 5, (((int)(p.Y / PlayerChain.WorldPerCell)) - y0) * Scale - 5, 11, (255, 230, 0));
            img.Fill((((int)(target.X / PlayerChain.WorldPerCell)) - x0) * Scale - 5, (((int)(target.Y / PlayerChain.WorldPerCell)) - y0) * Scale - 5, 11, (255, 255, 255));
            img.Save(args[0]);
            Console.WriteLine($"resim: {args[0]}  (hucre {x0},{y0} - {x1},{y1}, {Scale} px/hucre)");
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static IEnumerable<(float X, float Y, bool Closed)> Blockages(IntPtr h, ulong area)
    {
        var seen = new HashSet<uint>();
        var one = new byte[1];
        foreach (var offset in new[] { EntityReader.AwakeMap, EntityReader.SleepingMap })
        {
            if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }
            foreach (var (id, entity) in EntityReader.WalkMap(h, map))
            {
                if (!seen.Add(id) || EntityReader.ReadInfo(h, entity) is not { } info) { continue; }
                if (!info.Components.TryGetValue("TriggerableBlockage", out var tb) ||
                    !info.Components.TryGetValue("Render", out var render) ||
                    LocalPlayer.ReadPosition(h, render) is not { } pos ||
                    !EntityReader.Read(h, tb + 0x30, one))
                {
                    continue;
                }

                yield return (pos.X, pos.Y, one[0] != 0);
            }
        }
    }

    /// <summary>Elle yazilan 24 bit BMP: MapScan'de System.Drawing yok.</summary>
    private sealed class Image(int width, int height)
    {
        private readonly byte[] pixels = new byte[width * height * 3];

        internal void Fill(int x, int y, int size, (int R, int G, int B) c)
        {
            for (var yy = Math.Max(0, y); yy < Math.Min(height, y + size); yy++)
            {
                for (var xx = Math.Max(0, x); xx < Math.Min(width, x + size); xx++)
                {
                    var i = ((yy * width) + xx) * 3;
                    this.pixels[i] = (byte)c.B;
                    this.pixels[i + 1] = (byte)c.G;
                    this.pixels[i + 2] = (byte)c.R;
                }
            }
        }

        internal void Save(string file)
        {
            var stride = ((width * 3) + 3) & ~3;
            using var f = new BinaryWriter(File.Create(file));
            f.Write((byte)'B'); f.Write((byte)'M');
            f.Write(54 + (stride * height)); f.Write(0); f.Write(54);
            f.Write(40); f.Write(width); f.Write(height); f.Write((short)1); f.Write((short)24);
            f.Write(0); f.Write(stride * height); f.Write(2835); f.Write(2835); f.Write(0); f.Write(0);
            var row = new byte[stride];
            for (var y = height - 1; y >= 0; y--)
            {
                Array.Clear(row);
                Array.Copy(this.pixels, y * width * 3, row, 0, width * 3);
                f.Write(row);
            }
        }
    }
}
