namespace Poe2Map;

/// <summary>
///     MapScan itemflags: yerdeki her WorldItem'in esya varligi (WorldItem +0x28) normal ReadInfo ile okunuyor mu, okunmuyorsa
///     +0x8C bayragi ve bayrak yok sayilinca yol / nadirlik okunuyor mu (21.11.2026: yeni dusen esyalarin nadirligi 0.5-5 sn
///     gec okunuyordu, oyun yazisini hemen gosterirken). Sadece okur.
/// </summary>
internal static class ItemFlagsCommand
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
                .FirstOrDefault().Area;

            var ok = 0;
            var flags = new Dictionary<string, int>();
            var shown = 0;
            foreach (var e in MinimapIconProbe.Collect(h, area))
            {
                if (!e.Info.Components.TryGetValue("WorldItem", out var world)) { continue; }

                var item = PlayerChain.ReadPointer(h, world + GroundUniqueReader.WorldItemEntity);
                if (EntityReader.ReadInfo(h, item) is not null) { ok++; continue; }

                var raw = new byte[0x90];
                var flag = EntityReader.Read(h, item, raw) ? $"{raw[0x8C]:X2}" : "okunamadi";
                var relaxed = EntityReader.ReadInfo(h, item, anyFlag: true);
                var rarity = relaxed is not null && relaxed.Components.TryGetValue("Mods", out var mods) && EntityReader.Read(h, mods + GroundUniqueReader.ModsRarity, raw)
                    ? $"r{BitConverter.ToInt32(raw, 0)}"
                    : "Mods yok";
                var key = $"bayrak {flag}, bayraksiz okuma {(relaxed is null ? "yok" : "var")}";
                flags[key] = flags.GetValueOrDefault(key) + 1;
                if (shown++ < 15) { Console.WriteLine($"  {item:X}  {key}  {relaxed?.Path ?? "-"}  {rarity}  bilesen {relaxed?.Components.Count ?? 0}"); }
            }

            Console.WriteLine($"normal okunan esya {ok}");
            foreach (var (k, n) in flags) { Console.WriteLine($"okunamayan: {k} = {n}"); }
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
