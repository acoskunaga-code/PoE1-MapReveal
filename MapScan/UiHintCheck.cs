namespace Poe2Map;

/// <summary>
///     Ucuncu taraf ipucunu (GameOffsets.dll sabitleri: MapElement.LargeMapOffset 0x330, SmallMapOffset 0x338)
///     KAYITLI goruntude YAPISAL olarak sinar. Ipucu kabul edilmez, yalnizca aramayi daraltir; aday cikarsa
///     canli bellekte bagimsiz testle dogrulanir (davranis: zoom / surukleme, Tab).
///
///     Sinav sayilara degil yapiya bakar: M ogesinin +large ve +small ofsetlerindeki iki FARKLI oge de
///     ebeveyn olarak M'yi gosteriyorsa M harita ogesidir, ikisi buyuk ve kucuk haritadir. Ortak ebeveyn
///     ofseti de ipucundan bagimsiz olarak buradan cikar.
///
///         uihint [goruntu | canli:<kendiAlani>] [large=330] [small=338] [kayma=0]    (hepsi hex)
///
///     canli:B8 = kendini gosteren alani +0xB8 olan nesnelerin canli goruntusu (selfscan'in buldugu aile).
///     kayma = ipucu ofsetlerine eklenen sabit (PoE1 DLL'inde OffsetBuffers = 0; bizim surumde 0xB0 olabilir).
/// </summary>
internal static class UiHintCheck
{
    // Buyuk haritadaki alanlarin ipucu konumlari (MapSubElement sabitleri); sadece degerleri gostermek icin.
    private const int ShiftOffset = 0x320;
    private const int DefaultShiftOffset = 0x328;
    private const int ZoomOffset = 0x364;

    private static int delta;

    internal static int Run(string[] args)
    {
        var spec = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "poe1-zoom-s0.bin");
        var large = args.Length > 1 ? Convert.ToInt32(args[1], 16) : 0x330;
        var small = args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0x338;
        delta = args.Length > 3 ? Convert.ToInt32(args[3], 16) : 0;

        var snap = LoadOrTake(spec);
        if (snap == null) { return 1; }

        Console.WriteLine($"{snap.Elements.Count} oge; ipucu: buyuk +0x{large:X}, kucuk +0x{small:X}, kayma +0x{delta:X}");

        var twoElements = 0;
        var found = new List<(ulong M, ulong L, ulong S, List<int> Parents)>();
        foreach (var (m, raw) in snap.Elements)
        {
            var l = Ptr(raw, large);
            var s = Ptr(raw, small);
            if (l == s || l == m || s == m) { continue; }
            if (!snap.Elements.TryGetValue(l, out var lraw) || !snap.Elements.TryGetValue(s, out var sraw)) { continue; }

            twoElements++;
            var parents = Slots(lraw, m).Intersect(Slots(sraw, m)).ToList();
            if (parents.Count > 0) { found.Add((m, l, s, parents)); }
        }

        Console.WriteLine($"iki farkli oge gosteren: {twoElements}; ikisi de M'ye geri bakan: {found.Count}");
        foreach (var (m, l, s, parents) in found.Take(20))
        {
            var lraw = snap.Elements[l];
            Console.WriteLine($"\nM = 0x{m:X}   buyuk = 0x{l:X}   kucuk = 0x{s:X}   ebeveyn alani: " +
                              string.Join(", ", parents.Select(p => "+0x" + p.ToString("X"))));
            Console.WriteLine($"  buyuk: shift ({Fl(lraw, ShiftOffset + delta)}, {Fl(lraw, ShiftOffset + delta + 4)})  " +
                              $"varsayilan ({Fl(lraw, DefaultShiftOffset + delta)}, {Fl(lraw, DefaultShiftOffset + delta + 4)})  " +
                              $"zoom {Fl(lraw, ZoomOffset + delta)}");
        }

        SiblingPairs(snap);
        Console.WriteLine("\n--- ipucundan bagimsiz: dogrudan alanlarla cocuklara bakan ve cocuklari geri bakan ogeler ---");
        Parents(snap);
        return 0;
    }

    /// <summary>
    ///     Buyuk / kucuk harita deseni, ipucu ofsetlerinden BAGIMSIZ: bir M ogesinin yan yana iki alani (s, s+8)
    ///     AYNI SINIFTAN iki ogeyi gosteriyor ve ikisinin de ebeveyni M. Ayni sinifin goruntude ancak birkac ornegi
    ///     olmasi gerekir (iki harita ayni tipten). Ebeveyn alani UiKidsCommand'daki canli bulgu (+0x1E0).
    /// </summary>
    private static void SiblingPairs(UiSnapshot snap)
    {
        var classCount = snap.Elements.Values.GroupBy(v => BitConverter.ToUInt64(v, 0)).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("\n--- ayni sinifli iki kardes, ebeveynde yan yana alanlarda (en cok 4 ornekli sinif) ---");
        foreach (var (m, raw) in snap.Elements)
        {
            for (var s = 0xC8; s + 16 <= raw.Length; s += 8)
            {
                var a = Ptr(raw, s);
                var b = Ptr(raw, s + 8);
                if (a == b || a == m || b == m) { continue; }
                if (!snap.Elements.TryGetValue(a, out var ar) || !snap.Elements.TryGetValue(b, out var br)) { continue; }
                if (Ptr(ar, UiKidsCommand.ParentOffset) != m || Ptr(br, UiKidsCommand.ParentOffset) != m) { continue; }

                var vt = Ptr(ar, 0);
                if (vt != Ptr(br, 0) || classCount[vt] > 4) { continue; }

                Console.WriteLine($"M 0x{m:X} (boyut 0x{Ptr(raw, 8):X})  alan +0x{s:X} / +0x{s + 8:X}  " +
                                  $"A 0x{a:X} boyut {Fl(ar, 0x268)}x{Fl(ar, 0x26C)}   B 0x{b:X} boyut {Fl(br, 0x268)}x{Fl(br, 0x26C)}   " +
                                  $"sinif ornegi {classCount[vt]}  A.boyut 0x{Ptr(ar, 8):X}");
            }
        }
    }

    /// <summary>
    ///     M'nin penceresinde birden cok alan, ayni alanla M'ye geri bakan ogeleri gosteriyorsa M bir ust ogedir
    ///     (adli cocuk alanlari). Geri bakan alan = cocugun ebeveyn alani; ofsetler gozlemden cikar.
    ///     Harita ogesi bu kumede, aralari 8 / 0x90 / 0x98 / 0xB0 olan bes alanla aranir (ipucundaki aralik).
    /// </summary>
    private static void Parents(UiSnapshot snap)
    {
        var found = new List<(ulong M, int Back, List<int> Slots)>();
        foreach (var (m, raw) in snap.Elements)
        {
            var byBack = new Dictionary<int, List<int>>();
            for (var o = 16; o + 8 <= raw.Length; o += 8)
            {
                var p = BitConverter.ToUInt64(raw, o);
                if (p == m || !snap.Elements.TryGetValue(p, out var child)) { continue; }

                foreach (var back in Slots(child, m))
                {
                    if (!byBack.TryGetValue(back, out var list)) { byBack[back] = list = new List<int>(); }
                    list.Add(o);
                }
            }

            foreach (var (back, slots) in byBack)
            {
                if (slots.Count >= 3) { found.Add((m, back, slots)); }
            }
        }

        Console.WriteLine($"en az 3 adli cocugu olan oge: {found.Count}");
        var backVotes = found.GroupBy(f => f.Back).OrderByDescending(g => g.Count()).Take(5)
            .Select(g => $"+0x{g.Key:X} x{g.Count()}");
        Console.WriteLine("cocugun ebeveyn alani oylari: " + string.Join(", ", backVotes));

        foreach (var (m, back, slots) in found.OrderByDescending(f => f.Slots.Count).Take(12))
        {
            Console.WriteLine($"  M 0x{m:X}  ebeveyn alani +0x{back:X}  {slots.Count} cocuk alani: " +
                              string.Join(" ", slots.Take(14).Select(s => s.ToString("X"))) + (slots.Count > 14 ? " ..." : ""));
        }

        Console.WriteLine("\nharita ogesi kalibi (alan, +8, +0x90, +0x98, +0xB0):");
        foreach (var (m, back, slots) in found)
        {
            foreach (var a in slots)
            {
                if (new[] { 8, 0x90, 0x98, 0xB0 }.All(d => slots.Contains(a + d)))
                {
                    Console.WriteLine($"  M 0x{m:X}  alanlar +0x{a:X} +0x{a + 8:X} +0x{a + 0x90:X} +0x{a + 0x98:X} +0x{a + 0xB0:X}  " +
                                      $"ebeveyn alani +0x{back:X}");
                }
            }
        }
    }

    /// <summary>
    ///     Zoom ipucunu (+0x364) dort kayitli zoom goruntusunde sinar: makul bir float, S0 == S3 ve arada
    ///     degisen. uizoom'un duzenli-ilerleme sinavindan GEVSEK; ipucu konumu sabit oldugu icin gevsek
    ///     sinav bile ayirt edici (alan sayisi ile birlikte okunur).
    /// </summary>
    internal static int Zoom()
    {
        var shots = new UiSnapshot[4];
        for (var i = 0; i < 4; i++)
        {
            var p = Path.Combine(Path.GetTempPath(), $"poe1-zoom-s{i}.bin");
            if (!File.Exists(p)) { Console.WriteLine($"goruntu yok: {p}"); return 1; }

            shots[i] = UiSnapshot.Load(p);
        }

        var rows = new List<string>();
        var plausible = 0;
        foreach (var (a, r0) in shots[0].Elements)
        {
            if (!shots[1].Elements.TryGetValue(a, out var r1) || !shots[2].Elements.TryGetValue(a, out var r2) ||
                !shots[3].Elements.TryGetValue(a, out var r3))
            {
                continue;
            }

            var z = new[] { r0, r1, r2, r3 }.Select(r => BitConverter.ToSingle(r, ZoomOffset)).ToArray();
            if (z.Any(v => !float.IsFinite(v) || v < 0.01f || v > 50f)) { continue; }

            plausible++;
            if (z[0] == z[3] && (z[1] != z[0] || z[2] != z[0]))
            {
                rows.Add($"0x{a:X}  zoom {z[0]:G5} -> {z[1]:G5} -> {z[2]:G5} -> {z[3]:G5}   " +
                         $"shift ({Fl(r0, ShiftOffset)}, {Fl(r0, ShiftOffset + 4)})  " +
                         $"varsayilan ({Fl(r0, DefaultShiftOffset)}, {Fl(r0, DefaultShiftOffset + 4)})");
            }
        }

        Console.WriteLine($"+0x{ZoomOffset:X}'te makul zoom degeri (4 goruntude de) olan oge: {plausible}; " +
                          $"S0 == S3 ve arada degisen: {rows.Count}");
        foreach (var row in rows.Take(30)) { Console.WriteLine("  " + row); }

        return 0;
    }

    /// <summary>Dosyadan yukler ya da "canli:&lt;hex&gt;" ise o kendi-alani ile canli goruntu alir.</summary>
    private static UiSnapshot? LoadOrTake(string spec)
    {
        if (!spec.StartsWith("canli:", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(spec)) { return UiSnapshot.Load(spec); }

            Console.WriteLine($"goruntu yok: {spec}");
            return null;
        }

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return null; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return null; }

        try
        {
            return UiSnapshot.Take(h, Convert.ToInt32(spec[6..], 16));
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static ulong Ptr(byte[] raw, int offset) => offset + 8 <= raw.Length ? BitConverter.ToUInt64(raw, offset) : 0;

    private static string Fl(byte[] raw, int offset) =>
        offset + 4 <= raw.Length ? BitConverter.ToSingle(raw, offset).ToString("G5") : "-";

    /// <summary>Penceredeki hangi 8 baytlik yuvalarin <paramref name="target" /> adresini tuttugu.</summary>
    private static IEnumerable<int> Slots(byte[] raw, ulong target)
    {
        for (var o = 0; o + 8 <= raw.Length; o += 8)
        {
            if (BitConverter.ToUInt64(raw, o) == target) { yield return o; }
        }
    }
}
