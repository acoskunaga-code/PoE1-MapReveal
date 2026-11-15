using System.Collections.Concurrent;
using System.Text;

namespace Poe2Map;

/// <summary>
///     MapScan strscan &lt;metin&gt; [en cok=30]: oyunun yigin bellegini UTF-16 metin icin tarar (ASCII harflerde buyuk/kucuk
///     ayrimsiz), her bulunan yerin butun cumlesini (onceki ve sonraki NUL'a kadar) basar. Sonra o cumlenin baslangicini
///     gosteren 8 baytlik isaretcileri arar ve isaretcinin bir arayuz ogesinin icinde olup olmadigina bakar (oge kendini
///     +0xB8'de gosterir). Sadece okur. Amac: ekranda gorunen ama agacta (uitext) bulunmayan yazilarin sahibini bulmak,
///     ornek: Atlas'taki "Select a Thread type" ve thread kombinasyonlarinin ipucu kutusu.
/// </summary>
internal static class StrScanCommand
{
    private const int ReadBlock = 1 << 20;
    private const int SelfOffset = 0xB8;

    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: strscan <metin> [en cok=30]"); return 2; }

        var needle = args[0];
        var max = args.Length > 1 ? int.Parse(args[1]) : 30;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var regions = MemoryRegions.Heap(h);
            var hits = FindText(h, regions, needle);
            Console.WriteLine($"\"{needle}\": {hits.Count} yer");

            var starts = new Dictionary<ulong, string>();
            foreach (var hit in hits.OrderBy(x => x).Take(max))
            {
                var (start, text) = Sentence(h, hit);
                if (!starts.TryAdd(start, text)) { continue; }

                Console.WriteLine($"{start:X} (+{hit - start:X})  \"{text.Replace("\n", "\\n").Replace("\r", "")}\"");
            }

            if (starts.Count == 0) { return 0; }

            var refs = FindPointers(h, regions, starts.Keys.ToHashSet());
            foreach (var (target, holders) in refs.OrderBy(x => x.Key))
            {
                foreach (var at in holders.Take(8))
                {
                    var owner = OwnerElement(h, at);
                    Console.WriteLine($"  {target:X} <- {at:X}{(owner is { } o ? $"  oge {o.Element:X} +{o.Offset:X}" : "")}");
                }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static List<ulong> FindText(IntPtr h, List<(ulong Base, long Size)> regions, string needle)
    {
        var pattern = Encoding.Unicode.GetBytes(needle);
        var hits = new ConcurrentBag<ulong>();
        var overlap = pattern.Length;

        Parallel.ForEach(regions, () => new byte[ReadBlock], (region, _, buf) =>
        {
            for (long off = 0; off < region.Size; off += ReadBlock - overlap)
            {
                var want = (int)Math.Min(ReadBlock, region.Size - off);
                var block = region.Base + (ulong)off;
                if (!Native.ReadProcessMemory(h, (IntPtr)block, buf, (IntPtr)want, out var got)) { continue; }

                var n = (int)(long)got;
                for (var i = 0; i + pattern.Length <= n; i += 2)
                {
                    if (Same(buf, i, pattern)) { hits.Add(block + (ulong)i); }
                }
            }

            return buf;
        }, _ => { });

        return hits.Distinct().ToList();
    }

    private static bool Same(byte[] buf, int at, byte[] pattern)
    {
        for (var k = 0; k < pattern.Length; k++)
        {
            var a = buf[at + k];
            var b = pattern[k];
            if (a == b) { continue; }
            if (k % 2 == 1 || (a | 0x20) != (b | 0x20) || (b | 0x20) < 'a' || (b | 0x20) > 'z') { return false; }
        }

        return true;
    }

    private static (ulong Start, string Text) Sentence(IntPtr h, ulong hit)
    {
        const int Back = 1200;
        const int Forward = 2400;
        var from = hit - Back;
        var buf = new byte[Back + Forward];
        if (!Native.ReadProcessMemory(h, (IntPtr)from, buf, (IntPtr)buf.Length, out _)) { return (hit, ""); }

        var s = Back;
        while (s >= 2 && (buf[s - 2] | buf[s - 1]) != 0) { s -= 2; }
        var e = Back;
        while (e + 2 <= buf.Length && (buf[e] | buf[e + 1]) != 0) { e += 2; }

        return (from + (ulong)s, Encoding.Unicode.GetString(buf, s, e - s));
    }

    /// <remarks><paramref name="step"/> 8'den kucukse hizasiz isaretciler de (dat satirlari 34 bayt gibi tek aralikli olabiliyor).</remarks>
    internal static Dictionary<ulong, List<ulong>> FindPointers(IntPtr h, List<(ulong Base, long Size)> regions, HashSet<ulong> targets,
        int step = 8)
    {
        var found = new ConcurrentDictionary<ulong, ConcurrentBag<ulong>>();
        var advance = step < 8 ? ReadBlock - 8 : ReadBlock;
        Parallel.ForEach(regions, () => new byte[ReadBlock], (region, _, buf) =>
        {
            for (long off = 0; off < region.Size; off += advance)
            {
                var want = (int)Math.Min(ReadBlock, region.Size - off);
                var block = region.Base + (ulong)off;
                if (!Native.ReadProcessMemory(h, (IntPtr)block, buf, (IntPtr)want, out var got)) { continue; }

                var n = (int)(long)got;
                for (var i = 0; i + 8 <= n && (step == 8 || i < advance || off + advance >= region.Size); i += step)
                {
                    var v = BitConverter.ToUInt64(buf, i);
                    if (targets.Contains(v)) { found.GetOrAdd(v, _ => new ConcurrentBag<ulong>()).Add(block + (ulong)i); }
                }
            }

            return buf;
        }, _ => { });

        return found.ToDictionary(x => x.Key, x => x.Value.OrderBy(a => a).ToList());
    }

    private static (ulong Element, int Offset)? OwnerElement(IntPtr h, ulong at)
    {
        var buf = new byte[0x600];
        var from = at - 0x5F8;
        if (!Native.ReadProcessMemory(h, (IntPtr)from, buf, (IntPtr)buf.Length, out _)) { return null; }

        for (var k = 0; k <= 0x5F8 - SelfOffset; k += 8)
        {
            var element = at - (ulong)k;
            var self = (int)(element + SelfOffset - from);
            if (self < 0 || self + 8 > buf.Length) { continue; }
            if (BitConverter.ToUInt64(buf, self) == element) { return (element, k); }
        }

        return null;
    }
}
