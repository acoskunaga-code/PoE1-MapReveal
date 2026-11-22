using System.Diagnostics;

namespace Poe2Map;

/// <summary>
///     MapScan droptime [saniye = 300]: yeni dusen esyanin her asamasi kac ms sonra okunabiliyor (21.11.2026, kullanici:
///     "esyalari gec ciziyor belirginlestiriyor"). 30 ms'de bir uyanik ve uyuyan varlik listesi geziliyor; baslangictan
///     sonra gelen her WorldItem icin: listeye girdigi an (0), varlik okunabildi (bilgi), esya varligi + nadirlik okundu
///     (nadirlik), oyunun yer yazisi agacta (yazi) ve gorunur (gorunur). Esya basina bir satir, 6 sn sonra ya da hepsi
///     bitince. Sadece okur.
/// </summary>
internal static class DropTimeCommand
{
    private sealed class Drop
    {
        internal uint Id;
        internal ulong Entity;
        internal string List = "";
        internal long Seen;
        internal long Info = -1, Rarity = -1, Label = -1, Visible = -1;
        internal string What = "?";
        internal bool Printed;
    }

    internal static int Run(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 300;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            ulong labels = 0;
            if (UiFinder.FindLargeMap(h, null, requireVisible: false) is { } map)
            {
                labels = AltarLabels.Resolve(h, AltarLabels.RootOf(h, map.Address), GroundLabelMap.ElementPath);
            }

            Console.WriteLine($"yer yazisi agaci {labels:X}");
            var area = FindArea(h);
            var known = new HashSet<(uint, ulong)>();
            var drops = new Dictionary<(uint, ulong), Drop>();
            var clock = Stopwatch.StartNew();
            var first = true;
            var walkTotal = 0.0;
            var walkMax = 0.0;
            var walks = 0;
            var lastArea = clock.ElapsedMilliseconds;

            while (clock.Elapsed.TotalSeconds < seconds)
            {
                var t0 = clock.Elapsed.TotalMilliseconds;
                var awake = EntityReader.ReadMapHeader(h, area + EntityReader.AwakeMap) is { } a ? EntityReader.WalkMap(h, a, 5000) : new();
                var asleep = EntityReader.ReadMapHeader(h, area + EntityReader.SleepingMap) is { } b ? EntityReader.WalkMap(h, b, 5000) : new();
                var walk = clock.Elapsed.TotalMilliseconds - t0;
                walkTotal += walk;
                walkMax = Math.Max(walkMax, walk);
                walks++;

                if (awake.Count == 0 && clock.ElapsedMilliseconds - lastArea > 2000)
                {
                    area = FindArea(h);
                    lastArea = clock.ElapsedMilliseconds;
                    known.Clear();
                    first = true;
                    Console.WriteLine($"alan yeniden bulundu {area:X}");
                    continue;
                }

                var now = clock.ElapsedMilliseconds;
                foreach (var (list, nodes) in new[] { ("uyanik", awake), ("uyuyan", asleep) })
                {
                    foreach (var (id, entity) in nodes)
                    {
                        if (!known.Add((id, entity)) || first) { continue; }

                        drops[(id, entity)] = new Drop { Id = id, Entity = entity, List = list, Seen = now };
                    }
                }

                first = false;
                var tree = labels != 0 && drops.Values.Any(d => !d.Printed && d.Info >= 0)
                    ? GroundLabelMap.Read(h, labels)
                    : new Dictionary<ulong, ulong>();

                foreach (var d in drops.Values.Where(d => !d.Printed).ToList())
                {
                    var age = now - d.Seen;
                    if (d.Info < 0)
                    {
                        if (EntityReader.ReadInfo(h, d.Entity) is { } info)
                        {
                            if (!info.Path.EndsWith("/WorldItem", StringComparison.Ordinal)) { drops.Remove((d.Id, d.Entity)); continue; }
                            if (info.Components.ContainsKey("WorldItem")) { d.Info = age; }
                        }
                        else if (age > 3000) { drops.Remove((d.Id, d.Entity)); continue; }
                    }

                    if (d.Info >= 0 && d.Rarity < 0 && Rarity(h, d.Entity) is { } what) { d.Rarity = age; d.What = what; }
                    if (d.Info >= 0 && d.Label < 0 && tree.TryGetValue(d.Entity, out var label)) { d.Label = age; }
                    if (d.Label >= 0 && d.Visible < 0 && tree.TryGetValue(d.Entity, out var l2) && UiFinder.Reread(h, l2) is { Visible: true }) { d.Visible = age; }

                    if ((d.Visible >= 0 || age > 6000) && d.Info >= 0)
                    {
                        d.Printed = true;
                        if (d.Rarity < 0) { d.What = "? " + Why(h, d.Entity); }
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {d.What,-40} liste {d.List}  bilgi {Ms(d.Info)}  nadirlik {Ms(d.Rarity)}  yazi {Ms(d.Label)}  gorunur {Ms(d.Visible)}");
                    }
                }

                if (walks % 1000 == 0) { Console.WriteLine($"   liste gezintisi ort {walkTotal / walks:F1} ms, en uzun {walkMax:F1} ms ({awake.Count} uyanik, {asleep.Count} uyuyan)"); }
                Thread.Sleep(30);
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static string Ms(long v) => v < 0 ? "-" : $"{v} ms";

    /// <summary>Nadirlik neden okunamadi: esya isaretcisi ve esya varliginin okunabilen kismi.</summary>
    private static string Why(IntPtr h, ulong entity)
    {
        if (EntityReader.ReadInfo(h, entity) is not { } info || !info.Components.TryGetValue("WorldItem", out var world)) { return "WorldItem okunamadi"; }

        var item = PlayerChain.ReadPointer(h, world + GroundUniqueReader.WorldItemEntity);
        if (!PlayerChain.LooksLikePointer(item)) { return $"esya isaretcisi {item:X}"; }

        return EntityReader.ReadInfo(h, item, anyFlag: true) is { } it ? $"esya {it.Path}" : $"esya {item:X} ReadInfo null";
    }

    private static ulong FindArea(IntPtr h) => TerrainFinder.Find(h, _ => { })
        .Select(t => t.StructAddress - GameLayout.TerrainInArea)
        .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
        .OrderByDescending(x => x.Count)
        .FirstOrDefault().Area;

    /// <summary>"Belts/Belt4 r2" gibi: esya yolunun sonu ve nadirlik; esya varligi ya da Mods henuz yoksa null.</summary>
    private static string? Rarity(IntPtr h, ulong entity)
    {
        if (EntityReader.ReadInfo(h, entity) is not { } info || !info.Components.TryGetValue("WorldItem", out var world)) { return null; }

        var item = PlayerChain.ReadPointer(h, world + GroundUniqueReader.WorldItemEntity);
        if (EntityReader.ReadInfo(h, item, anyFlag: true) is not { } it) { return null; }

        var tail = it.Path[(it.Path.LastIndexOf('/') + 1)..];
        if (!it.Components.TryGetValue("Mods", out var mods)) { return $"{tail} (Mods yok)"; }

        var raw = new byte[4];
        if (!EntityReader.Read(h, mods + GroundUniqueReader.ModsRarity, raw)) { return null; }

        var rarity = BitConverter.ToInt32(raw, 0);
        var name = rarity == 3 && GroundUniqueReader.Classify(h, info) is { Length: > 0 } u ? $" = {u}" : "";
        return $"{tail} r{rarity}{name}";
    }
}
