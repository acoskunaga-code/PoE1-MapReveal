namespace Poe2Map;

/// <summary>
///     MapScan stats-watch [saniye]: oyuncunun Stats bileseninden iki kademe icinde (kimlik, deger) tamsayi
///     ciftlerinden olusan vektorleri bulur (kimlik 1..60000, en az 20 eleman) ve hepsini yarim saniyede bir okuyup
///     degisen degerleri zamaniyla basar. Sadece okur. Ekranda gosterilmeyen bir sayaci (Wildwood "fuel") bulmak
///     icin: kullanici dolasirken azalan, kure alinca artan kimlik aranan sayac.
/// </summary>
internal static class StatsWatchProbe
{
    internal static int Run(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 300;
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

            // Oyuncu olunce varlik yeniden kuruluyor: oyuncu ve vektorleri bes saniyede bir yeniden bulunuyor
            // (alan + 0x970 = yerel oyuncu). Vektor ayni kalirsa onceki degerler korunuyor.
            var playerEntity = 0UL;
            var vectors = new List<(string Where, ulong Holder)>();
            var previous = new Dictionary<ulong, Dictionary<int, int>>();
            var nextPlayer = DateTime.MinValue;
            var trend = new Dictionary<int, (int Down, int Up, int Last)>();
            var nextSummary = DateTime.UtcNow.AddSeconds(60);
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                if (DateTime.UtcNow >= nextPlayer)
                {
                    nextPlayer = DateTime.UtcNow.AddSeconds(5);
                    var entity = PlayerChain.ReadPointer(h, area.Area + GameLayout.LocalPlayerInArea);
                    if (entity != playerEntity && EntityReader.ReadInfo(h, entity) is { } info)
                    {
                        playerEntity = entity;
                        vectors.Clear();
                        foreach (var (name, address) in info.Components) { Find(h, address, name, 0, vectors, new HashSet<ulong>()); }

                        Console.WriteLine($"{DateTime.Now:HH:mm:ss.f} oyuncu {entity:X}, {vectors.Count} aday vektor: " +
                                          string.Join("  ", vectors.Select(v => $"{v.Where} ({Read(h, v.Holder)?.Count ?? 0} cift)")));
                        foreach (var (_, holder) in vectors)
                        {
                            if (!previous.ContainsKey(holder)) { previous[holder] = Read(h, holder) ?? new Dictionary<int, int>(); }
                        }
                    }
                }

                Thread.Sleep(500);
                var stamp = DateTime.Now.ToString("HH:mm:ss.f");
                foreach (var (where, holder) in vectors)
                {
                    if (Read(h, holder) is not { } now) { continue; }

                    var old = previous[holder];
                    foreach (var (id, value) in now)
                    {
                        if (!old.TryGetValue(id, out var was)) { Console.WriteLine($"{stamp} {where} + {id} = {value}"); }
                        else if (was != value)
                        {
                            Console.WriteLine($"{stamp} {where}   {id}: {was} -> {value}");
                            var t = trend.GetValueOrDefault(id);
                            trend[id] = value < was ? (t.Down + 1, t.Up, value) : (t.Down, t.Up + 1, value);
                        }
                    }

                    foreach (var id in old.Keys.Where(k => !now.ContainsKey(k))) { Console.WriteLine($"{stamp} {where} - {id} (was {old[id]})"); }

                    previous[holder] = now;
                }

                if (DateTime.UtcNow >= nextSummary)
                {
                    nextSummary = DateTime.UtcNow.AddSeconds(60);
                    Summary(trend);
                }
            }

            Summary(trend);
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Kimlik basina kac kez azaldi / artti: fuel gibi bir sayac cok kez azalir, kure alininca artar.</summary>
    private static void Summary(Dictionary<int, (int Down, int Up, int Last)> trend)
    {
        Console.WriteLine($"=== ozet {DateTime.Now:HH:mm:ss}: en cok degisen kimlikler ===");
        foreach (var (id, t) in trend.OrderByDescending(x => x.Value.Down + x.Value.Up).Take(25))
        {
            Console.WriteLine($"  {id,6}: azalma {t.Down,4}  artis {t.Up,4}  son {t.Last}");
        }

        Console.Out.Flush();
    }

    private static void Find(IntPtr h, ulong address, string where, int depth, List<(string, ulong)> into, HashSet<ulong> seen)
    {
        if (!seen.Add(address)) { return; }

        var buf = new byte[0x200];
        if (!EntityReader.Read(h, address, buf)) { return; }

        for (var o = 0x10; o + 16 <= buf.Length; o += 8)
        {
            var holder = address + (ulong)o;
            if (Read(h, holder) is { Count: >= 20 } && !into.Any(v => v.Item2 == holder))
            {
                into.Add(($"{where}+{o:X}", holder));
                continue;
            }

            var p = BitConverter.ToUInt64(buf, o);
            if (depth < 1 && PlayerChain.LooksLikePointer(p)) { Find(h, p, $"{where}+{o:X} ->", depth + 1, into, seen); }
        }
    }

    /// <summary>Adresteki (bas, son) vektoru (kimlik, deger) ciftleri olarak okur; uymuyorsa null.</summary>
    private static Dictionary<int, int>? Read(IntPtr h, ulong holder)
    {
        var pair = new byte[16];
        if (!EntityReader.Read(h, holder, pair)) { return null; }

        var first = BitConverter.ToUInt64(pair, 0);
        var last = BitConverter.ToUInt64(pair, 8);
        if (!PlayerChain.LooksLikePointer(first) || last <= first || (last - first) % 8 != 0 || last - first > 8 * 6000) { return null; }

        var raw = new byte[(int)(last - first)];
        if (!EntityReader.Read(h, first, raw)) { return null; }

        var result = new Dictionary<int, int>();
        for (var i = 0; i + 8 <= raw.Length; i += 8)
        {
            var id = BitConverter.ToInt32(raw, i);
            if (id is <= 0 or > 60000 || !result.TryAdd(id, BitConverter.ToInt32(raw, i + 4))) { return null; }
        }

        return result;
    }
}
