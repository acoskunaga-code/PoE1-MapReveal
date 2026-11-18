namespace Poe2Map;

/// <summary>
///     MapScan entsnap &lt;dosya&gt; [saniye aralik] [adet]: alandaki butun varliklari (uyanik + uyuyan) satir satir yazar:
///     numara, liste, yol, konum, bilesenler, TriggerableBlockage +0x30; yaninda zemin izgarasini &lt;dosya&gt;.grid olarak.
///     Aralik ve adet verilirse her seferinde &lt;dosya&gt;-SSmmss.txt ile tekrarlar (alan bir kez bulunur).
///     Iki an arasindaki fark (yeni/kaybolan varlik, donen +0x30, degisen zemin) bir olayin neyle yapildigini gosterir.
///     17.11.2026: Hive Fortress kulesinde 27/27'de "sadece 1 yoldan cikilabiliyor, diger yollar duvarla kapaniyor";
///     overlay yolu o duvarlarin icinden ciziyordu. Sadece okur.
/// </summary>
internal static class EntitySnapshotCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: entsnap <dosya> [aralik sn] [adet]"); return 2; }
        var every = args.Length >= 2 && int.TryParse(args[1], out var e) ? e : 0;
        var count = args.Length >= 3 && int.TryParse(args[2], out var c) ? c : 1;

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            if (LocalPlayer.Locate(h, TerrainFinder.Find(h, null), null) is not { } live)
            {
                Console.Error.WriteLine("Canli alan bulunamadi.");
                return 1;
            }

            var area = live.Area.StructAddress - GameLayout.TerrainInArea;
            for (var i = 0; i < count; i++)
            {
                if (i > 0) { Thread.Sleep(every * 1000); }
                var file = count > 1 ? $"{args[0]}-{DateTime.Now:HHmmss}.txt" : args[0];
                var n = Write(h, area, live, file);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {n} varlik -> {file}");
                if (TerrainFinder.TryReadAt(h, live.Area.StructAddress) is not { } t) { continue; }

                var raw = new byte[t.DataLength];
                if (EntityReader.Read(h, t.DataStart, raw)) { File.WriteAllBytes(file + ".grid", raw); }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static int Write(IntPtr h, ulong area, LocalPlayer.Found live, string file)
    {
        var lines = new List<string>();
        var seen = new HashSet<uint>();
        var one = new byte[1];
        var player = LocalPlayer.ReadPosition(h, live.Render);
        lines.Add($"# oyuncu {(player is { } p ? $"{p.X:F0} {p.Y:F0}" : "?")}  izgara {live.Area.GridX}x{live.Area.GridY} satir {live.Area.BytesPerRow}");

        foreach (var (label, offset) in new[] { ("uyanik", EntityReader.AwakeMap), ("uyuyan", EntityReader.SleepingMap) })
        {
            if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }
            foreach (var (id, entity) in EntityReader.WalkMap(h, map))
            {
                if (!seen.Add(id) || EntityReader.ReadInfo(h, entity) is not { } info) { continue; }

                var pos = info.Components.TryGetValue("Render", out var render) && LocalPlayer.ReadPosition(h, render) is { } q
                    ? $"{q.X:F0}\t{q.Y:F0}"
                    : "?\t?";
                var tb = info.Components.TryGetValue("TriggerableBlockage", out var block) && EntityReader.Read(h, block + 0x30, one)
                    ? one[0].ToString("X2")
                    : "-";
                lines.Add($"{id}\t{label}\t{info.Path}\t{pos}\t{tb}\t{string.Join(",", info.Components.Keys.OrderBy(k => k))}");
            }
        }

        File.WriteAllLines(file, lines);
        return lines.Count - 1;
    }
}
