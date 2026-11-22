namespace Poe2Map;

/// <summary>
///     MapScan hidden: bu alandaki canavarlarin kaci cizilir (EntityReader.TryReadMonster), kaci acilmamis Legion diye
///     gizleniyor; ayrica Legion disinda "hidden_monster"/"frozen_in_time" tasiyip yine de cizilenler (orn. Eldritch).
///     Yola gore gruplu. Sadece okur. 19.11.2026, Legion'a daraltma 21.11.2026.
/// </summary>
internal static class HiddenMonstersCommand
{
    internal static int Run(string[] args)
    {
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

            if (args.Length > 0 && args[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
            {
                return Watch(h, area.Area, args.Length > 1 && int.TryParse(args[1], out var s) ? s : 300);
            }

            var drawn = 0;
            var hidden = new List<string>();
            var drawnWithBuff = new List<string>();
            foreach (var e in MinimapIconProbe.Collect(h, area.Area).Where(e => e.Info.IsMonster))
            {
                var row = $"{e.Info.Path}  {(e.Awake ? "uyanik" : "uyuyan")}";
                if (EntityReader.TryReadMonster(h, e.Entity, e.Info, out _))
                {
                    drawn++;
                    if (e.Info.Components.TryGetValue("Buffs", out var b) && HiddenMonsters.HasHiddenBuff(h, b)) { drawnWithBuff.Add(row); }
                    continue;
                }

                if (HiddenMonsters.IsUnopenedLegion(h, e.Info)) { hidden.Add(row); }
            }

            Console.WriteLine($"cizilen canavar {drawn}, gizlenen acilmamis Legion {hidden.Count}");
            Print(hidden);
            Console.WriteLine($"cizilen ama gizleme buff'i tasiyan (Legion disi) {drawnWithBuff.Count}");
            Print(drawnWithBuff);
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>
    ///     MapScan hidden watch [saniye]: Legion canavarlarinin buff durumunu saniyede bir sayar, degisince bir satir yazar
    ///     (Legion'a tiklayinca hangi buff kalkiyor: hidden_monster mi, frozen_in_time mi). 21.11.2026.
    /// </summary>
    private static int Watch(IntPtr h, ulong area, int seconds)
    {
        var end = Environment.TickCount64 + seconds * 1000L;
        var last = "";
        while (Environment.TickCount64 < end)
        {
            var counts = new Dictionary<string, int>();
            foreach (var e in MinimapIconProbe.Collect(h, area).Where(e => HiddenMonsters.IsLegion(e.Info)))
            {
                var marks = e.Info.Components.TryGetValue("Buffs", out var b) ? HiddenMonsters.Read(h, b) : HiddenMonsters.Marks.None;
                var drawn = EntityReader.TryReadMonster(h, e.Entity, e.Info, out _);
                var key = $"{(marks == HiddenMonsters.Marks.None ? "buffsuz" : marks.ToString().Replace(", ", "+"))}{(drawn ? "/cizilen" : "")}";
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            var line = string.Join("  ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"));
            if (line != last)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  legion: {(line.Length == 0 ? "yok" : line)}");
                last = line;
            }

            Thread.Sleep(1000);
        }

        return 0;
    }

    private static void Print(List<string> rows)
    {
        foreach (var g in rows.GroupBy(x => x).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {g.Count(),4}  {g.Key}");
        }
    }
}
