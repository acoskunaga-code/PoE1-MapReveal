namespace Poe2Map;

/// <summary>
///     Sanal tabloya gore nesne goruntusu. "+0x08 == kendi adresi" sinavi PoE1'de UI ogelerini yakalamiyor
///     (selfscan: sanal tablolu nesnelerde o alan +0x08'de sadece 13 nesne), bu yuzden sinav tersine cevrildi:
///
///         gecis 1  : butun bellekte moduldeki bir adrese giden her 8 baytlik degeri say
///         eleme    : en az minInstances kez gecen ve kendisi de (ilk qword'u modul isaretcisi olan) bir dizi
///                    gibi duran adresler = sanal tablo; cok sik gecenler (ayirici dugumleri vb.) atilir
///         gecis 2  : ilk qword'u bu sanal tablolardan biri olan her adresin ilk Window baytini sakla
///
///     Tekil siniflar (arayuz koku gibi) burada yok; ebeveyn zincirinden sonradan eklenir (UiClassProbe).
/// </summary>
internal static class ClassSnapshot
{
    private const int Block = 1 << 20;
    private const int MaxClassSize = 4000;
    private const int MaxObjects = 400000;

    internal static UiSnapshot Take(IntPtr h, ulong moduleBase, ulong moduleEnd, int minInstances)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
        var window = UiSnapshot.Window;
        var pass1 = System.Diagnostics.Stopwatch.StartNew();
        var global = new Dictionary<ulong, int>();

        Parallel.ForEach(
            TerrainLayoutProbe.Regions(h),
            options,
            () => (Buf: new byte[Block], Local: new Dictionary<ulong, int>()),
            (region, _, state) =>
            {
                ForEachBlock(h, region, state.Buf, 16, (buf, n, _) =>
                {
                    for (var i = 0; i + 8 <= n; i += 8)
                    {
                        var v = BitConverter.ToUInt64(buf, i);
                        if (v >= moduleBase && v < moduleEnd && (v & 7) == 0)
                        {
                            state.Local[v] = state.Local.GetValueOrDefault(v) + 1;
                        }
                    }
                });
                return state;
            },
            state =>
            {
                lock (global)
                {
                    foreach (var (k, v) in state.Local) { global[k] = global.GetValueOrDefault(k) + v; }
                }
            });

        // sanal tablo adayi: yeterince cok, cok fazla degil, ve gosterdigi ilk qword da modulde (fonksiyon isaretcisi dizisi)
        var vtables = new HashSet<ulong>();
        var probe = new byte[8];
        foreach (var (v, count) in global)
        {
            if (count < minInstances || count > MaxClassSize) { continue; }
            if (!EntityReader.Read(h, v, probe)) { continue; }

            var first = BitConverter.ToUInt64(probe, 0);
            if (first >= moduleBase && first < moduleEnd) { vtables.Add(v); }
        }

        Console.WriteLine($"modul isaretcisi degeri: {global.Count}; sanal tablo adayi sinif: {vtables.Count} " +
                          $"({pass1.Elapsed.TotalSeconds:F1} sn)");

        var snapshot = new UiSnapshot();
        var taken = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.ForEach(
            TerrainLayoutProbe.Regions(h),
            options,
            () => new byte[Block],
            (region, _, buf) =>
            {
                ForEachBlock(h, region, buf, window, (b, n, baseAddress) =>
                {
                    for (var i = 0; i + window <= n; i += 8)
                    {
                        var vt = BitConverter.ToUInt64(b, i);
                        if (vt < moduleBase || vt >= moduleEnd || !vtables.Contains(vt)) { continue; }

                        // ConcurrentDictionary.Count her cagrida tum kilitleri alir; sadece eslesmede, sayacla
                        if (Interlocked.Increment(ref taken) > MaxObjects) { return; }

                        var raw = new byte[window];
                        Buffer.BlockCopy(b, i, raw, 0, window);
                        snapshot.Elements[baseAddress + (ulong)i] = raw;
                    }
                });
                return buf;
            },
            _ => { });

        Console.WriteLine($"goruntu: {snapshot.Elements.Count} nesne ({clock.Elapsed.TotalSeconds:F1} sn)");
        return snapshot;
    }

    /// <summary>Bolgeyi Block'luk parcalarla okur; ardisik parcalar <paramref name="overlap" /> bayt biner.</summary>
    private static void ForEachBlock(IntPtr h, (ulong Base, long Size) region, byte[] buf, int overlap,
        Action<byte[], int, ulong> body)
    {
        for (long off = 0; off < region.Size; off += Block - overlap)
        {
            var want = (int)Math.Min(Block, region.Size - off);
            if (want < overlap + 8) { break; }
            if (!Native.ReadProcessMemory(h, (IntPtr)(region.Base + (ulong)off), buf, (IntPtr)want, out var got) ||
                (long)got < overlap + 8)
            {
                continue;
            }

            body(buf, (int)(long)got, region.Base + (ulong)off);
        }
    }
}
