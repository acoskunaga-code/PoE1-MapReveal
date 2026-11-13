namespace Poe2Map;

/// <summary>
///     Birkac UI ogesinin ham baytlarini, kullanicinin yonlendirdigi asamalarla karsilastirir: hangi alan Tab'la,
///     zoom'la, kaydirmayla degisiyor? Hedef kucuk: 1KB'lik birkac nesne, bu yuzden tarama gerekmiyor.
///
///     Asama basinda N bip (N = asama + 1), her asama 7 sn; sonunda 3 ornek (150 ms arayla) alinir, uc ornek ayni
///     degilse alan o asamada "gurultulu" sayilir ve karsilastirmaya girmez.
///
///         1 bip  bekle, hicbir sey yapma
///         2 bip  Tab: haritayi AC
///         3 bip  numpad + tusuna 3 kez bas   (PoE1'de zoom tekerlekle degil numpad +/- ile; tekerlek denendi, bir sey degismedi)
///         4 bip  numpad - tusuna 3 kez bas   (asama 2'deki degerlere donmeli)
///         5 bip  haritayi SURUKLE: once sag-sol, sonra yukari-asagi
///         6 bip  Tab: haritayi KAPAT
///
///         elemwatch <hexadres> [<hexadres> ...]
/// </summary>
internal static class ElemWatchProbe
{
    private const int Window = 0x400;
    private const int PhaseMs = 7000;
    private const int Phases = 6;

    private static readonly string[] Says =
    {
        "bekle, hicbir sey yapma", "Tab: haritayi AC", "numpad + x3", "numpad - x3",
        "haritayi SURUKLE (sag-sol, yukari-asagi)", "Tab: haritayi KAPAT",
    };

    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: elemwatch <hexadres> [<hexadres> ...]"); return 2; }

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var addrs = args.Select(a => Convert.ToUInt64(a, 16)).ToArray();
            // [oge][asama] -> kararli degerler (u32 dizisi) ya da null (okunamadi); kararsiz alan = uint.MaxValue isareti ayri maske
            var values = new uint[addrs.Length][][];
            var stable = new bool[addrs.Length][][];
            for (var e = 0; e < addrs.Length; e++)
            {
                values[e] = new uint[Phases][];
                stable[e] = new bool[Phases][];
            }

            for (var p = 0; p < Phases; p++)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] asama {p + 1}/{Phases}: {Says[p]}");
                Cue(p + 1);
                Thread.Sleep(PhaseMs - 1200 - (p + 1) * 300);
                for (var e = 0; e < addrs.Length; e++) { Sample(h, addrs[e], values[e], stable[e], p); }
            }

            Console.WriteLine("\nbitti");
            for (var e = 0; e < addrs.Length; e++) { Report(addrs[e], values[e], stable[e]); }
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static void Sample(IntPtr h, ulong address, uint[][] values, bool[][] stable, int phase)
    {
        var shots = new byte[3][];
        for (var s = 0; s < 3; s++)
        {
            shots[s] = new byte[Window];
            if (!EntityReader.Read(h, address, shots[s])) { shots[s] = null!; }
            Thread.Sleep(150);
        }

        values[phase] = new uint[Window / 4];
        stable[phase] = new bool[Window / 4];
        if (shots[0] == null || shots[1] == null || shots[2] == null) { return; }

        for (var o = 0; o < Window / 4; o++)
        {
            var a = BitConverter.ToUInt32(shots[0], o * 4);
            values[phase][o] = a;
            stable[phase][o] = a == BitConverter.ToUInt32(shots[1], o * 4) && a == BitConverter.ToUInt32(shots[2], o * 4);
        }
    }

    private static void Report(ulong address, uint[][] values, bool[][] stable)
    {
        Console.WriteLine($"\n=== 0x{address:X} ===  (asama: 1 bekle | 2 Tab ac | 3 numpad+ | 4 numpad- | 5 surukle | 6 Tab kapat; ~ = kararsiz)");
        var rows = 0;
        for (var o = 0; o < Window / 4 && rows < 80; o++)
        {
            var changed = false;
            for (var p = 1; p < Phases; p++)
            {
                if (stable[p][o] && stable[p - 1][o] && values[p][o] != values[p - 1][o]) { changed = true; }
            }

            if (!changed) { continue; }

            rows++;
            var cells = Enumerable.Range(0, Phases).Select(p => (stable[p][o] ? "" : "~") + Show(values[p][o]));
            Console.WriteLine($"  +0x{o * 4,-5:X} {string.Join("  ", cells)}");
        }

        if (rows == 0) { Console.WriteLine("  hicbir alan degismedi (ya da hepsi kararsiz)"); }
    }

    private static string Show(uint raw)
    {
        var f = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
        return float.IsFinite(f) && (f == 0 || MathF.Abs(f) is >= 1e-5f and <= 1e7f) && raw != 0
            ? f.ToString("G6")
            : "0x" + raw.ToString("X");
    }

    private static void Cue(int times)
    {
        for (var i = 0; i < times; i++)
        {
            try { Console.Beep(880, 180); } catch { Thread.Sleep(180); }
            Thread.Sleep(120);
        }
    }
}
