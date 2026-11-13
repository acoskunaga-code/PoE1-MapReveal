using System.Diagnostics;

namespace Poe2Map;

/// <summary>
///     UI oge yerlesimini ogrenir; PoE2'nin sabit offsetlerini (parent +0xB8, boyut +0x270, kaydirma +0x350,
///     varsayilan kaydirma +0x358, zoom +0x390) varsaymaz. UiFinder.Find PoE1'de 67 bin ogenin hicbirini
///     buyuk harita olarak kabul etmedi: boyut ya da zoom alani baska yerde.
///
///     1. Parent: hangi ofsetin degeri BASKA BIR OGENIN adresi olan oge orani en yuksek.
///     2. Buyuk harita imzasi: tam ekrana yakin bir boyut cifti, (0, -20) civarinda bir varsayilan kaydirma
///        cifti ve yakininda zoom benzeri bir float. Bunlari birlikte tasiyan baska bir sey yok.
/// </summary>
internal static class UiLayoutProbe
{
    internal static int Run(string[] args)
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (handle == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var sw = Stopwatch.StartNew();
            var snapshot = UiSnapshot.Take(handle);
            Console.WriteLine($"{snapshot.Elements.Count} oge, {sw.Elapsed.TotalSeconds:F1} sn");

            ParentCandidates(snapshot);
            LargeMapSignature(snapshot);
            TextInventory(snapshot);
            return 0;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static void ParentCandidates(UiSnapshot snapshot)
    {
        var counts = new int[UiSnapshot.Window / 8];
        foreach (var raw in snapshot.Elements.Values)
        {
            for (var k = 2; k < counts.Length; k++)
            {
                if (snapshot.Elements.ContainsKey(BitConverter.ToUInt64(raw, k * 8))) { counts[k]++; }
            }
        }

        Console.WriteLine("\ndegeri baska bir ogenin adresi olan alanlar (parent adayi; PoE2: +0xB8):");
        foreach (var k in Enumerable.Range(2, counts.Length - 2).OrderByDescending(k => counts[k]).Take(6))
        {
            Console.WriteLine($"  +0x{k * 8:X}  %{100.0 * counts[k] / snapshot.Elements.Count:F1}");
        }
    }

    internal static void LargeMapSignature(UiSnapshot snapshot)
    {
        var rows = new List<(ulong Address, int S, float W, float H, int D, string Zooms)>();

        foreach (var (address, raw) in snapshot.Elements)
        {
            var s = -1;
            float w = 0, hh = 0;
            // Pencerenin tamami: PoE1'de taban sinif daha kucukse alanlar PoE2'den onde olur.
            for (var o = 0x40; o <= 0x468; o += 4)
            {
                var a = BitConverter.ToSingle(raw, o);
                var b = BitConverter.ToSingle(raw, o + 4);
                if (a is >= 800 and <= 20000 && b is >= 500 and <= 20000 && a * b > w * hh) { s = o; w = a; hh = b; }
            }

            if (s < 0) { continue; }

            for (var d = 0x40; d <= 0x470; d += 4)
            {
                var x = BitConverter.ToSingle(raw, d);
                var y = BitConverter.ToSingle(raw, d + 4);
                if (!(MathF.Abs(x) < 5f && MathF.Abs(y + 20f) < 5f)) { continue; }

                var zooms = new List<string>();
                for (var z = Math.Max(0x40, d - 0x40); z <= Math.Min(UiSnapshot.Window - 4, d + 0x80); z += 4)
                {
                    var f = BitConverter.ToSingle(raw, z);
                    if (f is > 0.05f and < 20f && f != 1f) { zooms.Add($"+0x{z:X}={f:F3}"); }
                }

                rows.Add((address, s, w, hh, d, string.Join(" ", zooms.Take(6))));
            }
        }

        Console.WriteLine($"\nbuyuk harita imzasi (boyut >= 800x500 + varsayilan kaydirma (0,-20)): {rows.Count} aday");
        foreach (var r in rows.OrderByDescending(r => r.W * r.H).Take(15))
        {
            Console.WriteLine($"  0x{r.Address:X}  boyut +0x{r.S:X} ({r.W:F0} x {r.H:F0})  " +
                              $"varsayilan kaydirma +0x{r.D:X}  zoom benzeri: {r.Zooms}");
        }

        Console.WriteLine("\nPoE2: boyut +0x270, kaydirma +0x350, varsayilan kaydirma +0x358, zoom +0x390");
    }

    /// <summary>8 hizali, NUL ile biten ASCII ya da UTF-16 metin; NUL'a ulasamazsa metin degil.</summary>
    private static string InlineText(byte[] raw, int at, bool wide)
    {
        var sb = new System.Text.StringBuilder();
        var step = wide ? 2 : 1;
        for (var i = at; i + step <= Math.Min(raw.Length, at + 64); i += step)
        {
            if (wide && raw[i + 1] != 0) { return ""; }

            var c = raw[i];
            if (c == 0) { return sb.ToString(); }
            if (c < 0x20 || c > 0x7E) { return ""; }

            sb.Append((char)c);
        }

        return "";
    }

    /// <summary>Ogelerin icindeki satir ici ad/kimlik metinleri; adi "map" iceren oge buyuk haritanin kendisi olabilir.</summary>
    private static void TextInventory(UiSnapshot snapshot)
    {
        var texts = new List<(ulong Address, int Off, string Text)>();
        foreach (var (address, raw) in snapshot.Elements)
        {
            for (var o = 0x10; o + 8 <= UiSnapshot.Window; o += 8)
            {
                var ascii = InlineText(raw, o, wide: false);
                if (ascii.Length >= 4) { texts.Add((address, o, ascii)); continue; }

                var wideText = InlineText(raw, o, wide: true);
                if (wideText.Length >= 4) { texts.Add((address, o, wideText)); }
            }
        }

        var maps = texts.Where(t => t.Text.Contains("map", StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"\nogelerin icinde {texts.Count} satir ici metin; icinde 'map' gecen {maps.Count}:");
        foreach (var t in maps.Take(40)) { Console.WriteLine($"  0x{t.Address:X}  +0x{t.Off:X}  \"{t.Text}\""); }
        if (maps.Count > 40) { Console.WriteLine($"  ... ve {maps.Count - 40} tane daha"); }

        Console.WriteLine("\nen cok metin tasiyan ofsetler:");
        foreach (var g in texts.GroupBy(t => t.Off).OrderByDescending(g => g.Count()).Take(5))
        {
            Console.WriteLine($"  +0x{g.Key:X}  {g.Count()} oge   ornek: {string.Join(" | ", g.Select(t => t.Text).Distinct().Take(4))}");
        }
    }
}
