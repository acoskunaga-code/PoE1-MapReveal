using System.Collections.Concurrent;

namespace Poe2Map;

/// <summary>
///     "Kendi adresini gosteren alan" NEREDE? PoE2'de UI ogelerinde +0x08'di; PoE1'de varsayim yanlis cikabilir
///     (+0x08 kumesinde 10'dan fazla ornekli tek bir sanal tablo bile yok).
///
///     Butun bellekte, sanal tablosu (+0x00) modulde olan her adres a icin a+s'te a'nin KENDISI duran s degerleri
///     (0x08..0x80) sayilir ve (s, sanal tablo) cifti basina toplanir. Gercek bir UI tipi, yuzlerce ornegiyle
///     tek bir (s, sanal tablo) satiri olarak one cikar; sanal tablo adresi (RVA) ayni zamanda Ghidra'da o tipin
///     yapicisina gidecek anahtardir.
///
///         selfscan
/// </summary>
internal static class SelfPointerScan
{
    private const int Block = 1 << 20;
    private const int Span = 0x408;
    private const int MaxSlot = 0x400;
    private const int MinClass = 20;

    internal static int Run()
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var moduleBase = (ulong)game.MainModule!.BaseAddress.ToInt64();
            var moduleEnd = moduleBase + (ulong)game.MainModule.ModuleMemorySize;
            var counts = new ConcurrentDictionary<(int, ulong), int>();
            var samples = new ConcurrentDictionary<(int, ulong), ulong>();

            Parallel.ForEach(
                TerrainLayoutProbe.Regions(h),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                () => new byte[Block],
                (region, _, buf) =>
                {
                    for (long off = 0; off < region.Size; off += Block - Span)
                    {
                        var want = (int)Math.Min(Block, region.Size - off);
                        if (want < Span + 16) { break; }
                        if (!Native.ReadProcessMemory(h, (IntPtr)(region.Base + (ulong)off), buf, (IntPtr)want, out var got) ||
                            (long)got < Span + 16)
                        {
                            continue;
                        }

                        var n = (int)(long)got;
                        for (var i = 0; i + Span <= n; i += 8)
                        {
                            var vt = BitConverter.ToUInt64(buf, i);
                            if (vt < moduleBase || vt >= moduleEnd) { continue; }

                            var here = region.Base + (ulong)off + (ulong)i;
                            for (var s = 8; s <= MaxSlot; s += 8)
                            {
                                if (BitConverter.ToUInt64(buf, i + s) != here) { continue; }

                                var key = (s, vt);
                                counts.AddOrUpdate(key, 1, (_, c) => c + 1);
                                samples.TryAdd(key, here);
                            }
                        }
                    }

                    return buf;
                },
                _ => { });

            Console.WriteLine($"(s, sanal tablo) cifti: {counts.Count}\n");
            foreach (var group in counts.GroupBy(kv => kv.Key.Item1).OrderBy(g => g.Key))
            {
                var big = group.Where(kv => kv.Value >= MinClass).OrderByDescending(kv => kv.Value).ToList();
                if (big.Count == 0) { continue; }

                Console.WriteLine($"kendini gosteren alan +0x{group.Key:X}: toplam {group.Sum(kv => kv.Value)} nesne, " +
                                  $"{big.Count} sinif >= {MinClass} ornek");
                foreach (var kv in big.Take(6))
                {
                    Console.WriteLine($"      vt+0x{kv.Key.Item2 - moduleBase:X}  {kv.Value,6} ornek   ornek 0x{samples[kv.Key]:X}");
                }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
