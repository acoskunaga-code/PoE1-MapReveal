using System.Numerics;
using System.Runtime.InteropServices;

namespace Poe2Map;

/// <summary>
///     Buyuk harita ogesini DAVRANISINDAN bulur: Tab ile acilip kapanan sey.
///
///     Uc goruntu: S0 (baslangic), S1 (Tab'tan sonra), S2 (ikinci Tab'tan sonra). Bir alan S0 == S2 ve
///     S1'den farkliysa Tab'a bagli: surekli ilerleyen animasyonlar ve rastgele degisenler S2'de S0'a
///     donmedigi icin elenir. Tab'a basilmasini arac kendisi algiliyor (GetAsyncKeyState, sadece tus
///     DURUMUNU okur) - kullanicinin ekrandaki yaziyi okumasi gerekmiyor.
///
///     Her goruntuden sonra bip calar (1, 2, 3 bip) ve SADECE bipten sonraki Tab sayilir; goruntu alinirken
///     erken basilan Tab goruntuyu bozmaz. Ilk denemede iki Tab arka arkaya basildigi icin ara goruntu
///     bozuk cikmisti.
///
///     Kullanim: uitoggle. 1 bip: Tab. 2 bip: Tab. 3 bip: bitti.
/// </summary>
internal static class UiToggleProbe
{
    private static int tabEdges;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    internal static int Run(string[] args)
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (handle == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            StartTabPoller();

            var s0 = UiSnapshot.Take(handle);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] goruntu 1 alindi ({s0.Elements.Count} oge). BIP: Tab'a bas.");
            Cue(1);
            if (!WaitForEdges(1)) { Console.WriteLine("zaman asimi: Tab algilanmadi (oyun on planda miydi?)"); return 2; }
            Thread.Sleep(900);

            var s1 = UiSnapshot.Take(handle);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] goruntu 2 alindi. IKI BIP: Tab'a tekrar bas.");
            Cue(2);
            if (!WaitForEdges(1)) { Console.WriteLine("zaman asimi: ikinci Tab algilanmadi"); return 2; }
            Thread.Sleep(900);

            var s2 = UiSnapshot.Take(handle);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] goruntu 3 alindi. UC BIP: bitti.");
            Cue(3);

            Report(s0, s1, s2);
            return 0;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>n bip. Sayaci ONCE sifirla ki bipten once basilmis Tab sayilmasin.</summary>
    private static void Cue(int times)
    {
        Volatile.Write(ref tabEdges, 0);
        for (var i = 0; i < times; i++)
        {
            try { Console.Beep(880, 180); } catch { Thread.Sleep(180); }
            Thread.Sleep(120);
        }
    }

    private static void StartTabPoller()
    {
        new Thread(() =>
        {
            var down = false;
            while (true)
            {
                var now = (GetAsyncKeyState(0x09) & 0x8000) != 0;
                if (now && !down) { Interlocked.Increment(ref tabEdges); }
                down = now;
                Thread.Sleep(15);
            }
        }) { IsBackground = true }.Start();
    }

    private static bool WaitForEdges(int target)
    {
        var until = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < until)
        {
            if (Volatile.Read(ref tabEdges) >= target) { return true; }
            Thread.Sleep(50);
        }

        return false;
    }

    private static void Report(UiSnapshot s0, UiSnapshot s1, UiSnapshot s2)
    {
        var common = s0.Elements.Keys
            .Where(a => s1.Elements.ContainsKey(a) && s2.Elements.ContainsKey(a))
            .ToList();
        Console.WriteLine($"\nS0 {s0.Elements.Count}  S1 {s1.Elements.Count}  S2 {s2.Elements.Count}  ortak {common.Count}");

        // (ofset, xor maskesi) -> bu degisimi gosteren ogeler. Sadece S0 == S2 != S1 olanlar.
        var groups = new Dictionary<(int Off, uint Xor), List<ulong>>();
        foreach (var a in common)
        {
            var r0 = s0.Elements[a];
            var r1 = s1.Elements[a];
            var r2 = s2.Elements[a];
            for (var o = 0; o + 4 <= UiSnapshot.Window; o += 4)
            {
                var v0 = BitConverter.ToUInt32(r0, o);
                var v1 = BitConverter.ToUInt32(r1, o);
                if (v1 == v0 || v0 != BitConverter.ToUInt32(r2, o)) { continue; }

                var key = (o, v0 ^ v1);
                if (!groups.TryGetValue(key, out var list)) { groups[key] = list = new List<ulong>(); }
                list.Add(a);
            }
        }

        var single = groups.Where(g => BitOperations.IsPow2(g.Key.Xor)).OrderByDescending(g => g.Value.Count).ToList();
        Console.WriteLine("\nTab ile degisip geri donen TEK BIT alanlari, en kalabaliklar:");
        foreach (var g in single.Take(6))
        {
            var up = g.Value.Count(a => (BitConverter.ToUInt32(s1.Elements[a], g.Key.Off) & g.Key.Xor) != 0);
            Console.WriteLine($"  +0x{g.Key.Off:X}  maske 0x{g.Key.Xor:X}  oge {g.Value.Count}  (S1'de 1 olan {up}, 0 olan {g.Value.Count - up})");
        }

        // Aday ogeler: (a) Tab ile tek bit degistirenler, (b) yalniz harita acikken var olanlar. Hepsi
        // arasindan tam ekrana yakin bir boyut cifti tasiyan buyuk haritanin kendisi olmali.
        var toggleInfo = new Dictionary<ulong, List<string>>();
        foreach (var g in single)
        {
            foreach (var a in g.Value)
            {
                if (!toggleInfo.TryGetValue(a, out var info)) { toggleInfo[a] = info = new List<string>(); }
                info.Add($"+0x{g.Key.Off:X}/0x{g.Key.Xor:X}");
            }
        }

        var created = s1.Elements.Keys
            .Where(a => !s0.Elements.ContainsKey(a) && !s2.Elements.ContainsKey(a))
            .ToHashSet();
        Console.WriteLine($"\nTab ile bit degistiren {toggleInfo.Count} oge; yalniz harita acikken var olan {created.Count} oge");

        var ranked = new List<(ulong Address, string Kind, int Off, float W, float H)>();
        foreach (var a in toggleInfo.Keys.Union(created))
        {
            var raw = s1.Elements[a];
            (int Off, float W, float H) best = (-1, 0, 0);
            for (var o = 0; o + 8 <= UiSnapshot.Window; o += 4)
            {
                var w = BitConverter.ToSingle(raw, o);
                var h = BitConverter.ToSingle(raw, o + 4);
                if (w is >= 300 and <= 20000 && h is >= 300 and <= 20000 && w * h > best.W * best.H) { best = (o, w, h); }
            }

            if (best.Off >= 0) { ranked.Add((a, created.Contains(a) ? "yeni" : "bit", best.Off, best.W, best.H)); }
        }

        Console.WriteLine("tam ekrana yakin (>=300 x >=300) float cifti tasiyanlar, en genisten:");
        foreach (var r in ranked.OrderByDescending(r => r.W * r.H).Take(8))
        {
            var bits = toggleInfo.TryGetValue(r.Address, out var l) ? string.Join(" ", l.Take(5)) : "";
            Console.WriteLine($"  0x{r.Address:X}  [{r.Kind}]  cift +0x{r.Off:X} ({r.W:F0} x {r.H:F0})  {bits}");
        }

        if (ranked.Count == 0) { Console.WriteLine("  (boyut cifti tasiyan aday yok)"); return; }

        var top = ranked.OrderByDescending(r => r.W * r.H).First().Address;
        s0.Elements.TryGetValue(top, out var t0);
        s2.Elements.TryGetValue(top, out var t2);
        DumpElement(top, t0, s1.Elements[top], t2);
    }

    private static void DumpElement(ulong address, byte[]? r0, byte[] r1, byte[]? r2)
    {
        if (r0 != null && r2 != null)
        {
            Console.WriteLine($"\nen genis oge 0x{address:X}: Tab ile degisen 4 baytlik alanlar (S0 -> S1, S2 == S0):");
            for (var o = 0; o + 4 <= UiSnapshot.Window; o += 4)
            {
                var v0 = BitConverter.ToUInt32(r0, o);
                var v1 = BitConverter.ToUInt32(r1, o);
                if (v1 != v0 && v0 == BitConverter.ToUInt32(r2, o))
                {
                    Console.WriteLine($"  +0x{o:X}  0x{v0:X8} -> 0x{v1:X8}   (float {BitConverter.UInt32BitsToSingle(v0):G6} -> {BitConverter.UInt32BitsToSingle(v1):G6})");
                }
            }
        }
        else
        {
            Console.WriteLine($"\nen genis oge 0x{address:X}: yalniz harita acikken var (S0/S2'de yok), fark listesi yok.");
        }

        Console.WriteLine("\nayni ogenin sifir olmayan 8 baytlari (S1):");
        for (var o = 0; o + 8 <= UiSnapshot.Window; o += 8)
        {
            var v = BitConverter.ToUInt64(r1, o);
            if (v == 0) { continue; }

            var lo = BitConverter.Int32BitsToSingle((int)(v & 0xFFFFFFFF));
            var hi = BitConverter.Int32BitsToSingle((int)(v >> 32));
            static bool Sane(float f) => float.IsFinite(f) && MathF.Abs(f) is >= 0.001f and <= 100000f;
            var note = Sane(lo) && Sane(hi) ? $"   (float {lo:G6}, {hi:G6})"
                : Sane(lo) && (v >> 32) == 0 ? $"   (float {lo:G6})"
                : v < 1_000_000UL ? $"   ({v})" : "";
            Console.WriteLine($"  +0x{o:X}  0x{v:X16}{note}");
        }
    }
}
