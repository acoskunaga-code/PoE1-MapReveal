namespace Poe2Map;

/// <summary>
///     Cikis yollari (ayar penceresi, Paths sekmesi): hangi cikisin yolu cizilsin. Cikis adina gore (kapinin yanindaki ad:
///     "The Sarn Ramparts", "Exit", "stairs down" ...); ayni ad baska alanlarda da gecerli. Her gorulen ad bir satir:
///
///         yol-acik   = The Sarn Ramparts       yolu cizilir
///         yol-kapali = Exit                    hic cizilmez (yol, kapinin isareti ve adi; 16.11.2026 oncesi isaret ve ad kaliyordu)
///
///     Ad degerde duruyor ki buyuk/kucuk harfi korunsun (anahtarlar okunurken kucuk harfe cevriliyor). Yeni gorulen ad
///     varsayilan olarak acik. Liste overlay oyunda cikis gordukce buyuyor (OverlayContext, ExitNamesSeen).
/// </summary>
internal sealed partial class OverlaySettings
{
    private const string PathOnKey = "yol-acik";
    private const string PathOffKey = "yol-kapali";

    /// <summary>Bugune kadar gorulen cikis adlari (panelde listelenen).</summary>
    internal SortedSet<string> SeenPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Haritada hic cizilmeyecek cikis adlari (yol, isaret, ad).</summary>
    internal HashSet<string> HiddenPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static void ReadPathLine(OverlaySettings settings, string key, string value)
    {
        if (value.Length == 0) { return; }

        settings.SeenPaths.Add(value);
        if (key == PathOffKey) { settings.HiddenPaths.Add(value); }
        else { settings.HiddenPaths.Remove(value); }
    }

    private void WritePathLines(List<string> lines)
    {
        lines.Add("");
        lines.Add("# Cikis yollari (ayar penceresi, Paths sekmesi): ada gore; kapali olan haritada hic cizilmez (yol, isaret, ad)");
        foreach (var name in this.SeenPaths)
        {
            lines.Add($"{(this.HiddenPaths.Contains(name) ? PathOffKey : PathOnKey)} = {name}");
        }
    }
}
