namespace Poe2Map;

/// <summary>
///     Buyuk harita ogesinin zoom alanini DAVRANISINDAN bulur: tekerlekle zoom yapinca tek bir alan DUZENLI
///     ilerler, baslangica donunce eski degerine doner. Gurultu (animasyon, sayac) bu uc sinavi birden gecmez.
///
///     Dort durum: S0 baslangic, S1 +2 tik, S2 +4 tik, S3 -4 tik (S0'a donmus). Harita ACIK olmali -
///     PoE1'de icerigi acilirken olusuyor. Ilk denemede iki durumla yetinmistik: oran siralamasi ilk 40'i
///     yazdigi icin 1'den buyuk oranli gercek zoom kesilmis olabilirdi; ve 0.266 -> 0.1 degisimi bir
///     animasyon kaydi cikti.
///
///     Adaylar: (a) float, her adimda ayni ORANLA (geometrik) ya da ayni FARKLA (dogrusal) ilerleyen;
///     (b) kucuk int32, ayni artisla ilerleyen. Goruntuler diske yaziliyor; uizoomdiff ayni analizi
///     yeni olcum istemeden tekrarlar.
///
///         1 bip -> 7 sn icinde tekerlegi YUKARI 2 tik cevir
///         2 bip -> 7 sn icinde tekrar YUKARI 2 tik
///         3 bip -> 10 sn icinde ASAGI 4 tik
///         4 bip -> bitti
/// </summary>
internal static class UiZoomProbe
{
    private static string Path(int i) => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poe1-zoom-s{i}.bin");

    private static string MetaPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "poe1-zoom-meta.txt");

    internal static int Run(string[] args)
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (handle == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            ulong moduleBase = 0, moduleSize = 0;
            try
            {
                moduleBase = (ulong)game.MainModule!.BaseAddress.ToInt64();
                moduleSize = (ulong)game.MainModule.ModuleMemorySize;
            }
            catch
            {
                // modul okunamazsa vtable isareti yazdirilmaz
            }

            File.WriteAllText(MetaPath, $"{moduleBase} {moduleSize}");

            var shots = new UiSnapshot[4];
            string[] says =
            {
                "tekerlegi YUKARI 2 tik cevir (7 sn)",
                "tekrar YUKARI 2 tik cevir (7 sn)",
                "tekerlegi ASAGI 4 tik cevir (10 sn)",
                "bitti",
            };
            int[] windows = { 7000, 7000, 10000, 0 };

            for (var i = 0; i < 4; i++)
            {
                shots[i] = UiSnapshot.Take(handle);
                shots[i].Save(Path(i));
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] goruntu {i + 1} alindi ({shots[i].Elements.Count} oge). " +
                                  $"{i + 1} BIP: {says[i]}.");
                Cue(i + 1);
                if (windows[i] > 0) { Thread.Sleep(windows[i]); }
            }

            Report(shots, moduleBase, moduleSize);
            return 0;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>Kayitli goruntulerle ayni analiz; kullanicidan yeni olcum istemez.</summary>
    internal static int Diff(string[] args)
    {
        var shots = new UiSnapshot[4];
        for (var i = 0; i < 4; i++)
        {
            if (!File.Exists(Path(i))) { Console.WriteLine($"kayitli goruntu yok: {Path(i)} (once uizoom)"); return 1; }
            shots[i] = UiSnapshot.Load(Path(i));
        }

        ulong moduleBase = 0, moduleSize = 0;
        if (File.Exists(MetaPath))
        {
            var parts = File.ReadAllText(MetaPath).Split(' ');
            if (parts.Length == 2) { ulong.TryParse(parts[0], out moduleBase); ulong.TryParse(parts[1], out moduleSize); }
        }

        Report(shots, moduleBase, moduleSize);
        return 0;
    }

    private static void Cue(int times)
    {
        for (var i = 0; i < times; i++)
        {
            try { Console.Beep(880, 180); } catch { Thread.Sleep(180); }
            Thread.Sleep(120);
        }
    }

    private static void Report(UiSnapshot[] s, ulong moduleBase, ulong moduleSize)
    {
        var common = s[0].Elements.Keys
            .Where(a => s[1].Elements.ContainsKey(a) && s[2].Elements.ContainsKey(a) && s[3].Elements.ContainsKey(a))
            .ToList();
        Console.WriteLine($"\ngoruntuler: {s[0].Elements.Count} / {s[1].Elements.Count} / {s[2].Elements.Count} / " +
                          $"{s[3].Elements.Count}   ortak {common.Count}");

        // Mutlak adres -> ilk gorulen (oge, ofset). Komsu ogelerin pencereleri ust uste biniyor;
        // ayni alan birden cok pencerede cikar, mutlak adresle teke indiriyoruz.
        var floats = new Dictionary<ulong, (ulong Element, int Off, float F0, float F1, float F2, string Kind)>();
        var ints = new Dictionary<ulong, (ulong Element, int Off, int I0, int I1, int I2)>();

        foreach (var a in common)
        {
            var r0 = s[0].Elements[a];
            var r1 = s[1].Elements[a];
            var r2 = s[2].Elements[a];
            var r3 = s[3].Elements[a];

            for (var o = 0; o + 4 <= UiSnapshot.Window; o += 4)
            {
                var b0 = BitConverter.ToUInt32(r0, o);
                var b1 = BitConverter.ToUInt32(r1, o);
                var b2 = BitConverter.ToUInt32(r2, o);
                if (b0 != BitConverter.ToUInt32(r3, o) || b1 == b0 || b2 == b0 || b1 == b2) { continue; }

                var abs = a + (ulong)o;

                var f0 = BitConverter.ToSingle(r0, o);
                var f1 = BitConverter.ToSingle(r1, o);
                var f2 = BitConverter.ToSingle(r2, o);
                if (float.IsFinite(f0) && float.IsFinite(f1) && float.IsFinite(f2) &&
                    MathF.Abs(f0) is >= 0.001f and <= 100000f && MathF.Abs(f1) is >= 0.001f and <= 100000f &&
                    MathF.Abs(f2) is >= 0.001f and <= 100000f &&
                    MathF.Sign(f0) == MathF.Sign(f1) && MathF.Sign(f1) == MathF.Sign(f2))
                {
                    var q1 = f1 / f0;
                    var q2 = f2 / f1;
                    var d1 = f1 - f0;
                    var d2 = f2 - f1;

                    if (MathF.Abs(q1 - 1f) >= 0.03f && MathF.Abs((q2 / q1) - 1f) < 0.06f)
                    {
                        floats.TryAdd(abs, (a, o, f0, f1, f2, "oran"));
                    }
                    else if (MathF.Abs(d1) >= 0.03f * MathF.Abs(f0) && MathF.Abs(d2 - d1) < 0.06f * MathF.Abs(d1))
                    {
                        floats.TryAdd(abs, (a, o, f0, f1, f2, "fark"));
                    }
                }

                var i0 = (int)b0;
                var i1 = (int)b1;
                var i2 = (int)b2;
                if (Math.Abs(i0) < 100000 && Math.Abs(i1) < 100000 && Math.Abs(i2) < 100000 && i1 - i0 == i2 - i1)
                {
                    ints.TryAdd(abs, (a, o, i0, i1, i2));
                }
            }
        }

        Console.WriteLine($"\nfloat adaylari (S0 == S3, adim adim duzenli): {floats.Count}");
        foreach (var (abs, c) in floats.OrderBy(kv => MathF.Abs(kv.Value.F1 / kv.Value.F0 - 1f)).Take(30))
        {
            Console.WriteLine($"  0x{abs:X}  (0x{c.Element:X}+0x{c.Off:X})  {c.F0:G6} -> {c.F1:G6} -> {c.F2:G6}  [{c.Kind}]" +
                              VtableMark(s[0].Elements[c.Element], moduleBase, moduleSize));
        }

        Console.WriteLine($"\nint adaylari (ayni artisla ilerleyen, kucuk): {ints.Count}");
        foreach (var (abs, c) in ints.Take(20))
        {
            Console.WriteLine($"  0x{abs:X}  (0x{c.Element:X}+0x{c.Off:X})  {c.I0} -> {c.I1} -> {c.I2}" +
                              VtableMark(s[0].Elements[c.Element], moduleBase, moduleSize));
        }
    }

    private static string VtableMark(byte[] raw, ulong moduleBase, ulong moduleSize)
    {
        if (moduleBase == 0) { return ""; }

        var vt = BitConverter.ToUInt64(raw, 0);
        return vt >= moduleBase && vt < moduleBase + moduleSize ? "  [vtable modulde]" : "";
    }
}
