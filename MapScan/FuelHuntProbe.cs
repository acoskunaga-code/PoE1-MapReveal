namespace Poe2Map;

/// <summary>
///     MapScan fuelhunt [saniye]: ekranda gosterilmeyen bir sayaci (Wildwood "fuel": dolastikca azalir, kure alinca
///     artar) bulmak icin genis izleme. Sadece okur. Izlenen bellek: alan nesnesinin ilk 0x1400 bayti, oradan bir
///     kademe gidilen her nesnenin 0x400 bayti ve oyuncunun butun bilesenleri (0x400). Yarim saniyede bir okunur;
///     her dort baytlik alanin (int ve float olarak) kac kez azaldigi / arttigi sayilir. Sonunda "cok kez azalan,
///     az kez artan" alanlar ilk ve son degerleriyle basilir. Oyuncu olup yeniden dogarsa bilesenleri yeniden alinir.
/// </summary>
internal static class FuelHuntProbe
{
    private sealed class Slot
    {
        internal float First;
        internal float Last;
        internal int Down;
        internal int Up;
        internal readonly List<string> Ups = new();
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
            var area = TerrainFinder.Find(h, _ => { })
                .Select(t => t.StructAddress - GameLayout.TerrainInArea)
                .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
                .OrderByDescending(x => x.Count)
                .FirstOrDefault().Area;

            var regions = Regions(h, area);
            Console.WriteLine($"alan {area:X}, {regions.Count} bolge izleniyor, {seconds} sn");

            var slots = new Dictionary<(string Name, int Offset, bool Float), Slot>();
            var previous = new Dictionary<string, byte[]>();
            var end = DateTime.UtcNow.AddSeconds(seconds);
            var nextRegions = DateTime.UtcNow.AddSeconds(10);
            var nextSummary = DateTime.UtcNow.AddSeconds(60);

            while (DateTime.UtcNow < end)
            {
                if (DateTime.UtcNow >= nextRegions)
                {
                    // Oyuncu oldu / yeniden dogduysa bilesen adresleri degisir.
                    regions = Regions(h, area);
                    nextRegions = DateTime.UtcNow.AddSeconds(10);
                }

                var stamp = DateTime.Now.ToString("HH:mm:ss.f");
                foreach (var (name, address, size) in regions)
                {
                    var now = new byte[size];
                    if (!EntityReader.Read(h, address, now)) { continue; }

                    if (previous.TryGetValue(name, out var before) && before.Length == size)
                    {
                        for (var o = 0; o + 4 <= size; o += 4)
                        {
                            var a = BitConverter.ToInt32(before, o);
                            var b = BitConverter.ToInt32(now, o);
                            if (a == b) { continue; }

                            Track(slots, (name, o, false), a, b, stamp);
                            var fa = BitConverter.ToSingle(before, o);
                            var fb = BitConverter.ToSingle(now, o);
                            if (float.IsFinite(fa) && float.IsFinite(fb) && Math.Abs(fa) < 1e7f && Math.Abs(fb) < 1e7f &&
                                (Math.Abs(fa) > 1e-3f || Math.Abs(fb) > 1e-3f))
                            {
                                Track(slots, (name, o, true), fa, fb, stamp);
                            }
                        }
                    }

                    previous[name] = now;
                }

                Thread.Sleep(500);

                // Ara ozet dakikada bir: kayit erken durdurulsa da sonuc elde kalsin.
                if (DateTime.UtcNow >= nextSummary)
                {
                    nextSummary = DateTime.UtcNow.AddSeconds(60);
                    Summary(slots, "ara ozet " + DateTime.Now.ToString("HH:mm:ss"));
                }
            }

            Summary(slots, "son ozet");
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static void Summary(Dictionary<(string Name, int Offset, bool Float), Slot> slots, string title)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title}: en cok azalan, az artan alanlar (azalma >= 6, artis <= 6) ===");
        foreach (var ((name, offset, isFloat), slot) in slots
                     .Where(x => x.Value.Down >= 6 && x.Value.Up <= 6)
                     .OrderByDescending(x => x.Value.Down - (x.Value.Up * 3))
                     .Take(40))
        {
            Console.WriteLine($"  {name}+{offset:X} {(isFloat ? "float" : "int  ")}  azalma {slot.Down,4} artis {slot.Up,3}  " +
                              $"{slot.First:0.###} -> {slot.Last:0.###}   artislar: {string.Join(", ", slot.Ups.Take(6))}");
        }

        Console.Out.Flush();
    }

    private static void Track(Dictionary<(string, int, bool), Slot> slots, (string, int, bool) key, float a, float b, string stamp)
    {
        if (!slots.TryGetValue(key, out var slot)) { slots[key] = slot = new Slot { First = a }; }

        slot.Last = b;
        if (b < a) { slot.Down++; }
        else
        {
            slot.Up++;
            if (slot.Ups.Count < 10) { slot.Ups.Add($"{stamp} {a:0.##}->{b:0.##}"); }
        }
    }

    /// <summary>Izlenecek bellek: alan nesnesi, ondan bir kademe nesneler, oyuncunun bilesenleri.</summary>
    private static List<(string Name, ulong Address, int Size)> Regions(IntPtr h, ulong area)
    {
        var list = new List<(string, ulong, int)> { ("alan", area, 0x1400) };
        var buf = new byte[0x1400];
        if (EntityReader.Read(h, area, buf))
        {
            var seen = new HashSet<ulong>();
            for (var o = 0; o + 8 <= buf.Length; o += 8)
            {
                var p = BitConverter.ToUInt64(buf, o);
                if (PlayerChain.LooksLikePointer(p) && p != area && seen.Add(p)) { list.Add(($"alan+{o:X}->", p, 0x400)); }
            }
        }

        var player = PlayerChain.ReadPointer(h, area + GameLayout.LocalPlayerInArea);
        if (EntityReader.ReadInfo(h, player) is { } info)
        {
            foreach (var (name, address) in info.Components) { list.Add(($"oyuncu.{name}", address, 0x400)); }
        }

        return list;
    }
}
