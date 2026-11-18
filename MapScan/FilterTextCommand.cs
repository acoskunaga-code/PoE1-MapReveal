using System.Collections.Concurrent;
using System.Text;

namespace Poe2Map;

/// <summary>
///     MapScan filtertext [kayit-dosyasi]: oyunun kullandigi loot filtresinin metni bellekte duruyor mu. FilterBlade'den
///     "Sync" ile gelen cevrimici filtre diskte okunabilir dosya olarak yok (My Games\Path of Exile\OnlineFilters bos,
///     production_Config.ini'de filtre anahtari yok; 18.11.2026). Yigin bellekte ASCII "$type->uniques $tier->" arar,
///     her bulunan yerin metnini (onceki ve sonraki NUL'a kadar) olcer; en uzununu istenirse diske yazar. Sadece okur.
/// </summary>
internal static class FilterTextCommand
{
    private const int ReadBlock = 1 << 20;

    internal static int Run(string[] args)
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            // Ikinci arguman: aranan metin; "u:" ile baslarsa UTF-16 (or. "u:Paua Amulet").
            var needle = args.Length > 1 ? args[1] : "$type->uniques $tier->";
            var pattern = needle.StartsWith("u:", StringComparison.Ordinal)
                ? Encoding.Unicode.GetBytes(needle[2..])
                : Encoding.ASCII.GetBytes(needle);
            var hits = new ConcurrentBag<ulong>();
            Parallel.ForEach(MemoryRegions.Heap(h), () => new byte[ReadBlock], (region, _, buf) =>
            {
                for (long off = 0; off < region.Size; off += ReadBlock - pattern.Length)
                {
                    var want = (int)Math.Min(ReadBlock, region.Size - off);
                    var block = region.Base + (ulong)off;
                    if (!Native.ReadProcessMemory(h, (IntPtr)block, buf, (IntPtr)want, out var got)) { continue; }

                    var n = (int)(long)got;
                    for (var i = buf.AsSpan(0, n).IndexOf(pattern); i >= 0;)
                    {
                        hits.Add(block + (ulong)i);
                        var next = buf.AsSpan(i + 1, n - i - 1).IndexOf(pattern);
                        i = next < 0 ? -1 : i + 1 + next;
                    }
                }

                return buf;
            }, _ => { });

            Console.WriteLine($"{hits.Count} yer");
            var texts = new Dictionary<ulong, (int Length, string Head)>();
            foreach (var hit in hits.Distinct().OrderBy(x => x))
            {
                var start = Edge(h, hit, -1);
                if (texts.ContainsKey(start)) { continue; }

                var end = Edge(h, hit, +1);
                var head = new byte[Math.Min(160, (int)(end - start))];
                Native.ReadProcessMemory(h, (IntPtr)start, head, (IntPtr)head.Length, out _);
                texts[start] = ((int)(end - start), Encoding.ASCII.GetString(head).Replace("\r", "").Replace("\n", " | "));
            }

            foreach (var (start, (length, head)) in texts.OrderByDescending(t => t.Value.Length).Take(12))
            {
                Console.WriteLine($"{start:X}  {length,9} bayt  \"{head}\"");
            }

            if (args.Length > 0 && texts.Count > 0)
            {
                var (best, (size, _)) = texts.OrderByDescending(t => t.Value.Length).First();
                var all = new byte[size];
                Native.ReadProcessMemory(h, (IntPtr)best, all, (IntPtr)size, out _);
                File.WriteAllBytes(args[0], all);
                Console.WriteLine($"yazildi: {args[0]} ({size} bayt)");
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Metnin kenari: yon -1 baslangic, +1 son. Yazdirilamayan bayt (NUL vb., satir sonlari ve tab haric) ya da 8 MB.</summary>
    private static ulong Edge(IntPtr h, ulong from, int direction)
    {
        var buf = new byte[4096];
        var at = from;
        for (var steps = 0; steps < 2048; steps++)
        {
            var block = direction < 0 ? at - (ulong)buf.Length : at;
            if (!Native.ReadProcessMemory(h, (IntPtr)block, buf, (IntPtr)buf.Length, out _)) { return at; }

            if (direction < 0)
            {
                for (var i = buf.Length - 1; i >= 0; i--)
                {
                    if (!Text(buf[i])) { return block + (ulong)i + 1; }
                }

                at = block;
            }
            else
            {
                for (var i = 0; i < buf.Length; i++)
                {
                    if (!Text(buf[i])) { return block + (ulong)i; }
                }

                at = block + (ulong)buf.Length;
            }
        }

        return at;
    }

    private static bool Text(byte b) => b is >= 0x20 and < 0x7F or (byte)'\r' or (byte)'\n' or (byte)'\t';
}
