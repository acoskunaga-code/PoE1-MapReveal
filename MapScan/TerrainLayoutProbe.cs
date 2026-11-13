using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Poe2Map;

/// <summary>
///     Zemin yapisinin YERLESIMINI ogrenir; hicbir sabit offset varsaymaz.
///
///     TerrainFinder PoE2'nin yerlesimini bilerek ariyor (dosem +0x18, vektor +0xD0, satirBayt +0x130);
///     PoE1'de 6 GB taramada 0 sonuc verdi. Burada degismez olani ariyoruz:
///
///         - iki kucuk tam sayi: dosem X ve Y (int64 cifti ya da int32 cifti)
///         - ayni yapinin icinde bir veri vektoru (baslangic, bitis); uzunluk = satirBayt * izgaraY
///         - ayni pencerede satirBayt'in kendisi (int32)
///
///     Uc kosul birden rastgele bellekte tutmaz. Bulununca alanlarin birbirine gore yerini yazdiriyoruz;
///     yeni bir oyun surumunde ya da baska bir oyunda yerlesim boylece kendiliginden ogrenilir.
///     Ofsetler VEKTOR ALANINA gore, isaretli: PoE2'de dosem -0xB8, satirBayt +0x60 olurdu.
/// </summary>
internal static class TerrainLayoutProbe
{
    private const int ReadBlock = 1 << 20;
    private const int Before = 0x200;   // dosem cifti vektor alanindan bu kadar once olabilir
    private const int After = 0x600;    // ... ya da bu kadar sonra
    private const int Overlap = Before + After;

    private readonly record struct Hit(
        ulong VectorField, ulong Begin, ulong End, long Tx, long Ty, long BytesPerRow,
        string Form, int RelPair, int RelBytesPerRow);

    /// <summary>Kullanim: terrainlearn [hucre/dosem = 23]</summary>
    internal static int Run(string[] args)
    {
        var cells = args.Length > 0 ? int.Parse(args[0]) : 23;

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (handle == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var sw = Stopwatch.StartNew();
            var regions = Regions(handle);
            var hits = new ConcurrentBag<Hit>();
            long scanned = 0;

            Parallel.ForEach(
                regions,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                () => new byte[ReadBlock],
                (region, _, buf) =>
                {
                    for (long off = 0; off < region.Size; off += ReadBlock - Overlap)
                    {
                        var want = (int)Math.Min(ReadBlock, region.Size - off);
                        if (want < Overlap + 16) { break; }
                        if (!Native.ReadProcessMemory(handle, (IntPtr)(region.Base + (ulong)off), buf,
                                (IntPtr)want, out var got) || (long)got < Overlap + 16)
                        {
                            continue;
                        }

                        Interlocked.Add(ref scanned, (long)got);
                        ScanBlock(buf, (int)(long)got, region.Base + (ulong)off, cells, hits);
                    }

                    return buf;
                },
                _ => { });

            var all = hits.GroupBy(h => h.VectorField).ToList();

            // Gercek yapi: veri bir heap isaretcisi (>4 GB), 0x18 yaninda ayni uzunlukta kardes vektor
            // (yurunebilirlik + arazi cifti) ve dosem cifti vektore yakin. Yuvarlak sayili sahte
            // adaylar (128x128 doku tablolari gibi) bu uc olcutu birden gecmiyor.
            bool HasSibling(IGrouping<ulong, Hit> g) =>
                all.Any(o => (o.Key == g.Key + 0x18 || o.Key + 0x18 == g.Key) &&
                             o.First().End - o.First().Begin == g.First().End - g.First().Begin);

            var groups = all
                .Where(g => g.First().Begin >= 0x100000000UL)
                .OrderByDescending(HasSibling)
                .ThenByDescending(g => g.Any(h => h.Form == "int64" && Math.Abs(h.RelPair) <= 0x100))
                .ThenByDescending(g => g.First().Tx * g.First().Ty)
                .ToList();

            Console.WriteLine($"{scanned / 1024 / 1024} MB tarandi, {all.Count} ham aday, {groups.Count} elemeden gecen, " +
                              $"{sw.Elapsed.TotalSeconds:F1} sn   (hucre/dosem {cells})");

            foreach (var g in groups.Take(6))
            {
                var h0 = g.First();
                Console.WriteLine();
                Console.WriteLine($"vektor alani 0x{g.Key:X}   veri 0x{h0.Begin:X}   uzunluk {h0.End - h0.Begin}" +
                                  $"   = satirBayt {h0.BytesPerRow} x {h0.Ty * cells}");

                foreach (var h in g.Distinct())
                {
                    Console.WriteLine($"    dosem {h.Tx}x{h.Ty} ({h.Form}) {Rel(h.RelPair),-7}   " +
                                      $"satirBayt int32 {Rel(h.RelBytesPerRow)}");
                }
            }

            if (groups.Count > 0) { Dump(handle, groups[0].Key, groups[0].First()); }
            else { Console.WriteLine("\nHic aday yok: hucre/dosem sabiti ya da vektor bicimi farkli olabilir."); }
            return 0;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static void ScanBlock(byte[] buf, int n, ulong blockBase, int cells, ConcurrentBag<Hit> hits)
    {
        var len8 = n & ~7;
        var q = MemoryMarshal.Cast<byte, long>(buf.AsSpan(0, len8));
        var d = MemoryMarshal.Cast<byte, int>(buf.AsSpan(0, len8));

        for (var pos = Before; pos + After <= len8; pos += 4)
        {
            // Bicim A: iki int64 (PoE2'deki gibi), 8 hizali.
            if ((pos & 7) == 0)
            {
                var tx = q[pos >> 3];
                var ty = q[(pos >> 3) + 1];
                if ((ulong)(tx - 4) <= 3996 && (ulong)(ty - 4) <= 3996)
                {
                    TryPair(q, d, pos, tx, ty, "int64", blockBase, cells, hits);
                }
            }

            // Bicim B: iki bitisik int32.
            var tx32 = d[pos >> 2];
            var ty32 = d[(pos >> 2) + 1];
            if ((uint)(tx32 - 4) <= 3996 && (uint)(ty32 - 4) <= 3996)
            {
                TryPair(q, d, pos, tx32, ty32, "int32", blockBase, cells, hits);
            }
        }
    }

    private static void TryPair(
        ReadOnlySpan<long> q, ReadOnlySpan<int> d, int pos, long tx, long ty, string form,
        ulong blockBase, int cells, ConcurrentBag<Hit> hits)
    {
        var gx = tx * cells;
        var gy = ty * cells;
        var minLen = gy * ((gx + 1) / 2);
        var maxLen = gy * (gx + 16);

        var w0 = (pos - Before + 7) >> 3;
        var w1 = ((pos + After) >> 3) - 1;

        for (var w = w0; w < w1; w++)
        {
            var begin = q[w];
            var length = q[w + 1] - begin;
            if (length < minLen || length > maxLen || length % gy != 0) { continue; }
            if ((ulong)begin < 0x10000UL || (ulong)begin > 0x7FFFFFFFFFFFUL) { continue; }

            var bytesPerRow = length / gy;

            // satirBayt'in kendisi ayni pencerede duruyor mu
            var found = -1;
            for (var k = (pos - Before) >> 2; k < (pos + After) >> 2; k++)
            {
                if (d[k] == bytesPerRow) { found = k; break; }
            }

            if (found < 0) { continue; }

            hits.Add(new Hit(
                blockBase + ((ulong)w << 3), (ulong)begin, (ulong)q[w + 1], tx, ty, bytesPerRow,
                form, pos - (w << 3), (found << 2) - (w << 3)));
        }
    }

    /// <summary>En buyuk adayin komsulugunu ve veri ornegini yazdirir: yerlesimi gozle okumak icin.</summary>
    private static void Dump(IntPtr handle, ulong field, Hit hit)
    {
        // Yapi, vektor alanindan ~0xD0 once basliyor (PoE2'deki gibi ise) ve ~0x140 boyunda.
        const int Back = 0xF0;
        const int Len = 0x1C0;

        var buf = new byte[Len];
        if (!Native.ReadProcessMemory(handle, (IntPtr)(field - Back), buf, (IntPtr)Len, out var got) ||
            (long)got != Len)
        {
            Console.WriteLine("\n(komsuluk okunamadi)");
            return;
        }

        Console.WriteLine("\nen buyuk adayin komsulugu (vektor alanina gore ofset, sifir olmayan 8 baytlar):");
        for (var i = 0; i < Len; i += 8)
        {
            var v = BitConverter.ToUInt64(buf, i);
            if (v == 0) { continue; }

            var lo = (int)(v & 0xFFFFFFFF);
            var hi = (int)(v >> 32);
            var note = v < 1_000_000UL
                ? $"   ({v})"
                : lo is >= 0 and < 100000 && hi is >= 0 and < 100000 ? $"   (iki int32: {lo}, {hi})" : "";
            Console.WriteLine($"  {Rel(i - Back),7}  0x{v:X16}{note}");
        }

        // Ortadaki 64 KB: PoE2'de hucreler dort bitlik, 0..5 degerli, ikisi bir baytta.
        var length = (long)(hit.End - hit.Begin);
        var sample = (int)Math.Min(length, 65536);
        var data = new byte[sample];
        var at = hit.Begin + (ulong)Math.Max(0, (length / 2) - (sample / 2));
        if (Native.ReadProcessMemory(handle, (IntPtr)at, data, (IntPtr)sample, out var g2) && (long)g2 == sample)
        {
            var ok = data.Count(b => (b & 0xF) <= 5 && (b >> 4) <= 5);
            Console.WriteLine($"\nveri ornegi (ortadan {sample} bayt): iki yarim bayti da 0..5 olan oran " +
                              $"%{100.0 * ok / sample:F1}, farkli bayt degeri {data.Distinct().Count()}");
            Console.WriteLine("ilk 32 bayt: " + BitConverter.ToString(data, 0, Math.Min(32, sample)));
        }
    }

    private static string Rel(int v) => v < 0 ? $"-0x{-v:X}" : $"+0x{v:X}";

    /// <summary>Yazilabilir ozel bellek bolgeleri (heap). TerrainFinder ile ayni olcut.</summary>
    internal static List<(ulong Base, long Size)> Regions(IntPtr handle) => MemoryRegions.Heap(handle);
}
