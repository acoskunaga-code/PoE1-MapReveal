namespace Poe2Map;

/// <summary>
///     Eldritch altar ayarlari (ayar penceresindeki "Altars" sekmesi):
///
///         altar-player = acik / kapali          "Player gains:" ile baslayan secenek yesil cerceve
///         altar-text   = Divine Orb | Mirror    bu yazilardan biri gecen secenek (kim kazanirsa kazansin) altin
///                                                cerceve; "|" ile ayrilmis, bos = hicbiri
///         altar-player-color / altar-text-color = #RRGGBB   iki cercevenin rengi
///
///     Satir hic yoksa varsayilan: Player acik, yazi listesi "Divine Orb".
/// </summary>
internal sealed partial class OverlaySettings
{
    private const string AltarPlayerKey = "altar-player";
    private const string AltarTextKey = "altar-text";
    private const char AltarTextSeparator = '|';

    /// <summary>Eldritch altarlarinda "Player gains:" ile baslayan secenegin kutusu cercevelensin mi.</summary>
    internal bool AltarPlayer { get; set; } = true;

    /// <summary>Altin cerceve icin aranan yazilar; buyuk/kucuk harf ve sayilar onemsiz (AltarLabels.Normalize).</summary>
    internal List<string> AltarPhrases { get; set; } = new() { "Divine Orb" };

    // Cerceve renkleri, "#RRGGBB" (Altars sekmesindeki renk dugmeleri).
    private const string AltarPlayerColorKey = "altar-player-color";
    private const string AltarTextColorKey = "altar-text-color";

    internal Color AltarPlayerColor { get; set; } = Color.FromArgb(40, 255, 110);

    internal Color AltarTextColor { get; set; } = Color.FromArgb(255, 200, 40);

    // "wildwood = acik / kapali": The Viridian Wildwood'da wisp'ler, yakit kureleri ve Funerary altarlari haritada.
    private const string WildwoodKey = "wildwood";

    internal bool Wildwood { get; set; } = true;

    private static List<string> ReadAltarPhrases(string value) =>
        value.Split(AltarTextSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>"#RRGGBB" -> renk; bozuksa null (varsayilan kalir).</summary>
    private static Color? ReadColor(string value) =>
        int.TryParse(value.TrimStart('#'), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var rgb)
            ? Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
            : null;

    private static string ColorText(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void WriteAltarLines(List<string> lines)
    {
        lines.Add("");
        lines.Add("# Eldritch altarlari (ayar penceresi, Altars sekmesi): Player gains yesil, yazilardan biri gecen altin");
        lines.Add($"{AltarPlayerKey} = {(this.AltarPlayer ? "acik" : "kapali")}");
        lines.Add($"{AltarPlayerColorKey} = {ColorText(this.AltarPlayerColor)}");
        lines.Add($"{AltarTextColorKey} = {ColorText(this.AltarTextColor)}");
        lines.Add($"{WildwoodKey} = {(this.Wildwood ? "acik" : "kapali")}");

        // Ayiracin kendisi yaziya girmesin: satir tekrar okunurken bolunurdu.
        var phrases = this.AltarPhrases.Select(p => p.Replace(AltarTextSeparator, ' ').Trim()).Where(p => p.Length > 0);
        lines.Add($"{AltarTextKey} = {string.Join($" {AltarTextSeparator} ", phrases)}");
    }
}
