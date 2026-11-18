using System.Globalization;

namespace Poe2Map;

/// <summary>
///     Yerdeki unique'ler (ayar penceresi, Uniques sekmesi), unique adina gore:
///
///         unique-goster = Mageblood            yerdeyse haritada adi yazilir
///         unique-yol    = Mageblood            yerdeyse haritada ona yol cizilir (adi da yazilir)
///         unique-uyari  = Mageblood            yerdeyse ekranin ustunde buyuk harflerle adi (harita kapaliyken de)
///         unique-yazi   = Segoe UI; 10; bold   haritadaki unique adlarinin yazi tipi (butun unique'ler icin)
///         unique-renk   = #FFA046              adlarin (ve yollarin, uyarinin) rengi
///         unique-uyari-boyut = 36              ekrandaki buyuk adin punto boyu
///
///     Satiri olmayan unique haritada yok: varsayilan hepsi kapali (kullanici 18.11.2026: "default olarak hepsini
///     kapat"; onceki "unique-gizli" satirlari okunmuyor). Ad degerde duruyor ki buyuk/kucuk harfi korunsun.
/// </summary>
internal sealed partial class OverlaySettings
{
    private const string UniqueShownKey = "unique-goster";
    private const string UniquePathKey = "unique-yol";
    private const string UniqueFontKey = "unique-yazi";
    private const string UniqueColorKey = "unique-renk";
    private const string UniqueAlertKey = "unique-uyari";
    private const string UniqueAlertSizeKey = "unique-uyari-boyut";
    private const string UniqueFrameKey = "unique-cerceve";

    /// <summary>Yerdeyken oyundaki yer yazisi altarlardaki gibi cercevelenecek unique'ler (unique-cerceve = ad).</summary>
    internal HashSet<string> FrameUniques { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal HashSet<string> ShownUniques { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal HashSet<string> PathUniques { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Yerdeyken ekranin ustunde buyuk harflerle adi yazilacak unique'ler (kullanici 18.11.2026).</summary>
    internal HashSet<string> AlertUniques { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal int UniqueAlertSize { get; set; } = 36;

    internal string UniqueFontName { get; set; } = "Segoe UI";

    internal float UniqueFontSize { get; set; } = 10f;

    internal bool UniqueFontBold { get; set; } = true;

    internal Color UniqueColor { get; set; } = Color.FromArgb(255, 160, 70);

    private static void ReadUniqueLine(OverlaySettings settings, string key, string value)
    {
        switch (key)
        {
            case UniqueShownKey when value.Length > 0:
                settings.ShownUniques.Add(value);
                break;
            case UniquePathKey when value.Length > 0:
                settings.PathUniques.Add(value);
                break;
            case UniqueColorKey when ReadColor(value) is { } color:
                settings.UniqueColor = color;
                break;
            case UniqueAlertKey when value.Length > 0:
                settings.AlertUniques.Add(value);
                break;
            case UniqueFrameKey when value.Length > 0:
                settings.FrameUniques.Add(value);
                break;
            case UniqueAlertSizeKey when int.TryParse(value, out var alertSize):
                settings.UniqueAlertSize = Math.Clamp(alertSize, 12, 120);
                break;
            case UniqueFontKey:
                var parts = value.Split(';', StringSplitOptions.TrimEntries);
                if (parts.Length > 0 && parts[0].Length > 0) { settings.UniqueFontName = parts[0]; }
                if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                {
                    settings.UniqueFontSize = Math.Clamp(size, 6f, 40f);
                }

                settings.UniqueFontBold = parts.Length <= 2 || parts[2].Equals("bold", StringComparison.OrdinalIgnoreCase);
                break;
        }
    }

    private void WriteUniqueLines(List<string> lines)
    {
        lines.Add("");
        lines.Add("# Yerdeki unique'ler (ayar penceresi, Uniques sekmesi): goster = adi yazilir, yol = yol cizilir; satiri olmayan kapali");
        lines.Add($"{UniqueFontKey} = {this.UniqueFontName}; {this.UniqueFontSize.ToString("0.#", CultureInfo.InvariantCulture)}; {(this.UniqueFontBold ? "bold" : "regular")}");
        lines.Add($"{UniqueColorKey} = {ColorText(this.UniqueColor)}");
        lines.Add($"{UniqueAlertSizeKey} = {this.UniqueAlertSize}");
        foreach (var name in this.ShownUniques.Order(StringComparer.OrdinalIgnoreCase)) { lines.Add($"{UniqueShownKey} = {name}"); }
        foreach (var name in this.PathUniques.Order(StringComparer.OrdinalIgnoreCase)) { lines.Add($"{UniquePathKey} = {name}"); }
        foreach (var name in this.AlertUniques.Order(StringComparer.OrdinalIgnoreCase)) { lines.Add($"{UniqueAlertKey} = {name}"); }
        foreach (var name in this.FrameUniques.Order(StringComparer.OrdinalIgnoreCase)) { lines.Add($"{UniqueFrameKey} = {name}"); }
    }

    /// <summary>Haritadaki unique adlarinin yazi tipi (cagiran Dispose etmeli).</summary>
    internal Font CreateUniqueFont()
    {
        try
        {
            return new Font(this.UniqueFontName, this.UniqueFontSize, this.UniqueFontBold ? FontStyle.Bold : FontStyle.Regular);
        }
        catch (ArgumentException)
        {
            return new Font("Segoe UI", this.UniqueFontSize, this.UniqueFontBold ? FontStyle.Bold : FontStyle.Regular);
        }
    }
}
