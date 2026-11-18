namespace Poe2Map;

/// <summary>
///     MapScan blockages [izle [saniye]]: TriggerableBlockage bilesenli varliklari (kapilar, Breach kulesinin
///     ArmBlockage kollari) listeler: konum, bilesenin ilk 0x80 bayti ve zemin izgarasinin cevresindeki degerleri.
///     "izle" modunda her yarim saniyede yeniden okur ve yalnizca DEGISEN bilesen baytlarini ve kapinin cevresindeki
///     zemin hucrelerini yazar: kapi/kol acilip kapandiginda hangi baytin dondugu ve oyunun zemin izgarasini kendisinin
///     guncelleyip guncellemedigi buradan gorulur.
///     17.11.2026: Dunes haritasindaki Hive Fortress kulesinde kollar kapandiginda overlay yolu kapali kollardan
///     geciriyordu; overlay butun TriggerableBlockage'lari her zaman acik kapi sayiyor. Sadece okur.
/// </summary>
internal static class BlockagesCommand
{
    private const int DumpBytes = 0x80;
    private const int Window = 7;

    private sealed record Item(uint Id, ulong Entity, string Name, float X, float Y, ulong Component);

    internal static int Run(string[] args)
    {
        var watch = args.Length >= 1 && args[0].Equals("izle", StringComparison.OrdinalIgnoreCase);
        var seconds = watch && args.Length >= 2 && int.TryParse(args[1], out var s) ? s : 120;

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            if (LocalPlayer.Locate(h, TerrainFinder.Find(h, null), null) is not { } live)
            {
                Console.Error.WriteLine("Canli alan bulunamadi - bir alanda misin?");
                return 1;
            }

            var t = live.Area;
            var area = t.StructAddress - GameLayout.TerrainInArea;
            var items = Collect(h, area);
            var player = LocalPlayer.ReadPosition(h, live.Render);
            Console.WriteLine($"alan {t.GridX}x{t.GridY}  oyuncu " +
                              (player is { } pp ? $"({pp.X:F0}, {pp.Y:F0})" : "?") + $"  {items.Count} engel");

            var raw = ReadTerrain(h, t);
            var dumps = new Dictionary<uint, byte[]>();
            foreach (var it in items.OrderBy(i => i.Name).ThenBy(i => i.X))
            {
                var dump = new byte[DumpBytes];
                EntityReader.Read(h, it.Component, dump);
                dumps[it.Id] = dump;
                var dist = player is { } p ? Math.Sqrt(((p.X - it.X) * (p.X - it.X)) + ((p.Y - it.Y) * (p.Y - it.Y))) : -1;
                Console.WriteLine();
                Console.WriteLine($"== {it.Name}  id {it.Id}  ({it.X:F0}, {it.Y:F0})  uzaklik {dist:F0}  bilesen {it.Component:X}");
                for (var o = 0; o < DumpBytes; o += 0x10)
                {
                    Console.WriteLine($"   +{o:X2}  {BitConverter.ToString(dump, o, 0x10).Replace('-', ' ')}");
                }

                if (raw is not null) { PrintCells(raw, t, it); }
            }

            if (!watch) { return 0; }

            Console.WriteLine();
            Console.WriteLine($"izleniyor ({seconds} sn): yalnizca degisenler yaziliyor");
            var end = Environment.TickCount64 + (seconds * 1000L);
            while (Environment.TickCount64 < end)
            {
                Thread.Sleep(500);
                var fresh = ReadTerrain(h, t);
                foreach (var it in items)
                {
                    var dump = new byte[DumpBytes];
                    if (!EntityReader.Read(h, it.Component, dump)) { continue; }
                    var old = dumps[it.Id];
                    var changed = Enumerable.Range(0, DumpBytes).Where(i => dump[i] != old[i]).ToList();
                    if (changed.Count > 0)
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss.f}  {it.Name} id {it.Id} ({it.X:F0}, {it.Y:F0}): " +
                                          string.Join("  ", changed.Select(i => $"+{i:X2} {old[i]:X2}->{dump[i]:X2}")));
                        dumps[it.Id] = dump;
                    }

                    if (raw is not null && fresh is not null)
                    {
                        var cells = CellChanges(raw, fresh, t, it);
                        if (cells > 0) { Console.WriteLine($"{DateTime.Now:HH:mm:ss.f}  {it.Name} id {it.Id}: cevresinde {cells} zemin hucresi degisti"); }
                    }
                }

                if (fresh is not null) { raw = fresh; }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static List<Item> Collect(IntPtr h, ulong area)
    {
        var items = new Dictionary<uint, Item>();
        foreach (var offset in new[] { EntityReader.AwakeMap, EntityReader.SleepingMap })
        {
            if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }
            foreach (var (id, entity) in EntityReader.WalkMap(h, map))
            {
                if (items.ContainsKey(id) || EntityReader.ReadInfo(h, entity) is not { } info) { continue; }
                if (!info.Components.TryGetValue("TriggerableBlockage", out var component)) { continue; }
                if (!info.Components.TryGetValue("Render", out var render)) { continue; }
                if (LocalPlayer.ReadPosition(h, render) is not { } p) { continue; }

                var at = info.Path.LastIndexOf('/');
                items[id] = new Item(id, entity, at >= 0 ? info.Path[(at + 1)..] : info.Path, p.X, p.Y, component);
            }
        }

        return items.Values.ToList();
    }

    private static byte[]? ReadTerrain(IntPtr h, TerrainFinder.Terrain t)
    {
        var raw = new byte[t.DataLength];
        return EntityReader.Read(h, t.DataStart, raw) ? raw : null;
    }

    private static int Cell(byte[] raw, TerrainFinder.Terrain t, int x, int y)
    {
        if (x < 0 || y < 0 || x >= t.GridX || y >= t.GridY) { return -1; }
        var index = ((long)y * t.BytesPerRow) + (x / 2);
        if (index >= raw.Length) { return -1; }
        return (x & 1) == 0 ? raw[index] & 0x0F : raw[index] >> 4;
    }

    private static (int X, int Y) GridOf(Item it) =>
        ((int)(it.X / PlayerChain.WorldPerCell), (int)(it.Y / PlayerChain.WorldPerCell));

    private static void PrintCells(byte[] raw, TerrainFinder.Terrain t, Item it)
    {
        var (gx, gy) = GridOf(it);
        Console.WriteLine($"   zemin ({gx}, {gy}) cevresi, {Window * 2 + 1}x{Window * 2 + 1} (0 = kapali):");
        for (var y = gy - Window; y <= gy + Window; y++)
        {
            var row = Enumerable.Range(gx - Window, (Window * 2) + 1).Select(x => Cell(raw, t, x, y) is var v and >= 0 ? v.ToString("X") : ".");
            Console.WriteLine("     " + string.Concat(row));
        }
    }

    private static int CellChanges(byte[] before, byte[] after, TerrainFinder.Terrain t, Item it)
    {
        var (gx, gy) = GridOf(it);
        var count = 0;
        for (var y = gy - (Window * 3); y <= gy + (Window * 3); y++)
        {
            for (var x = gx - (Window * 3); x <= gx + (Window * 3); x++)
            {
                if (Cell(before, t, x, y) != Cell(after, t, x, y)) { count++; }
            }
        }

        return count;
    }
}
