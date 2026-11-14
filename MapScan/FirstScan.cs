using System.Collections.Concurrent;
using System.Diagnostics;

namespace Poe2Map;

/// <summary>
///     Ilk konumlama icin TEK bellek taramasi: zemin yapilari (TerrainFinder) ve buyuk harita ogesi (UiFinder) ayni
///     gecisten. Ikisi ayni bolgeleri (MemoryRegions.Heap) ayri ayri tariyordu; 14.11.2026 olcum: her biri ~4.9 GB, 3.8 sn,
///     ilk harita acilistan ~8 sn sonra geliyordu (bir kullanici "harita 5-10 sn gec yukleniyor" dedi). Burada her blok
///     bir kez okunuyor ve her 8 baytlik konumda iki sinav yapiliyor (TerrainFinder.Check, UiFinder.CheckLargeMap).
///
///     Bloklar ortusuyor (en buyuk sinav penceresi kadar); bir konum yalnizca "kendi" blogunda sinaniyor, son blok haric
///     ortusen kuyruk bir sonraki bloga birakiliyor. Ayni adres yine de iki kez gelirse sozlukte tekilleniyor.
///
///     Sinamak: `MapScan firstscan` (sure ve sonuc, ayri taramalarla karsilastirmali).
/// </summary>
internal static class FirstScan
{
    private const int ReadBlock = 1 << 20;

    internal static (List<TerrainFinder.Terrain> Terrains, UiFinder.UiElement? Element) Run(IntPtr handle, Action<string>? log = null)
    {
        var sw = Stopwatch.StartNew();
        var overlap = Math.Max(TerrainFinder.StructSpan, UiFinder.Span);
        var terrains = new ConcurrentDictionary<ulong, TerrainFinder.Terrain>();
        var elements = new ConcurrentDictionary<ulong, UiFinder.UiElement>();
        long scanned = 0;

        Parallel.ForEach(
            MemoryRegions.Heap(handle),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            () => new byte[ReadBlock],
            (region, _, buf) =>
            {
                for (long off = 0; off < region.Size; off += ReadBlock - overlap)
                {
                    var want = (int)Math.Min(ReadBlock, region.Size - off);
                    if (want < TerrainFinder.StructSpan) { break; }

                    var block = region.Base + (ulong)off;
                    if (!Native.ReadProcessMemory(handle, (IntPtr)block, buf, (IntPtr)want, out var got) ||
                        (long)got < TerrainFinder.StructSpan)
                    {
                        continue;
                    }

                    var n = (int)(long)got;
                    Interlocked.Add(ref scanned, n);

                    // Son blok degilse ortusen kuyruk bir sonraki blogun basinda sinanacak.
                    var last = off + want >= region.Size;
                    var limit = last ? n : Math.Min(n, ReadBlock - overlap);
                    for (var i = 0; i < limit; i += 8)
                    {
                        var here = block + (ulong)i;
                        if (i + TerrainFinder.StructSpan <= n && TerrainFinder.Check(handle, buf, i, here) is { } t)
                        {
                            terrains.TryAdd(here, t);
                        }

                        if (i + UiFinder.Span <= n && UiFinder.CheckLargeMap(buf, i, here) is { } e)
                        {
                            elements.TryAdd(here, e);
                        }
                    }
                }

                return buf;
            },
            _ => { });

        var element = UiFinder.PickLargeMap(handle, elements.Values, requireVisible: false);
        log?.Invoke($"tek tarama: {scanned / 1024 / 1024} MB, {terrains.Count} zemin yapisi, harita ogesi adayi {elements.Count} " +
                    $"({(element is { } el ? $"0x{el.Address:X}" : "yok")}), {sw.Elapsed.TotalSeconds:F1} sn");
        return (terrains.Values.ToList(), element);
    }

    /// <summary>MapScan firstscan: tek tarama ile iki ayri taramayi sure ve sonuc olarak karsilastirir.</summary>
    internal static int Cli()
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var one = Run(h, Console.WriteLine);
            var sw = Stopwatch.StartNew();
            var terrains = TerrainFinder.Find(h, null);
            var element = UiFinder.FindLargeMap(h, null, requireVisible: false);
            Console.WriteLine($"ayri ayri: {terrains.Count} zemin, oge {(element is { } e ? $"0x{e.Address:X}" : "yok")}, {sw.Elapsed.TotalSeconds:F1} sn");

            var same = one.Terrains.Select(t => t.StructAddress).ToHashSet().SetEquals(terrains.Select(t => t.StructAddress)) &&
                       one.Element?.Address == element?.Address;
            Console.WriteLine(same ? "sonuclar ayni" : "SONUCLAR FARKLI");
            return same ? 0 : 1;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
