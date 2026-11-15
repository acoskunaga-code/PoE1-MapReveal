namespace Poe2Map;

/// <summary>
///     Fate ayarlari (ayar penceresindeki "Fates" sekmesi):
///
///         fate-labels     = acik / kapali          Fate panelinde kartlarin ustune odul yazilari
///         fate-text       = Divine | Unique         bu yazilardan biri gecen odul o renkte yaziliyor ve kartin kutusu
///                                                  o renkte cerceveleniyor; "|" ile ayrilmis, bos = hicbiri
///         fate-text-color = #RRGGBB                 o renk
///
///     Satir hic yoksa varsayilan: yazilar acik, aranan yazi yok, renk altin.
/// </summary>
internal sealed partial class OverlaySettings
{
    private const string FateRewardsKey = "fate-labels";
    private const string FateTextKey = "fate-text";
    private const string FateTextColorKey = "fate-text-color";

    /// <summary>Fate panelinde kartlarin ustune odul yazilari yazilsin mi.</summary>
    internal bool FateRewards { get; set; } = true;

    /// <summary>Cerceve icin aranan yazilar; buyuk/kucuk harf ve sayilar onemsiz (AltarLabels.Normalize).</summary>
    internal List<string> FatePhrases { get; set; } = new();

    internal Color FateTextColor { get; set; } = Color.FromArgb(255, 200, 40);

    // "fate-oneri = acik / kapali": Invoke Fates panelinde en iyi kartlar ve tiklama sirasi (FatePlanner).
    // "fate-sira = Currency | Threads | ...": oneri icin odul sirasi, en iyi en basta; bos = hepsi esit.
    private const string FatePlanKey = "fate-oneri";
    private const string FateRankKey = "fate-sira";

    internal bool FatePlan { get; set; } = true;

    internal List<string> FateRanking { get; set; } = FatePlanner.DefaultRanking.ToList();

    private static void ReadFateLine(OverlaySettings settings, string key, string value)
    {
        switch (key)
        {
            case FatePlanKey:
                settings.FatePlan = !value.Equals("kapali", StringComparison.OrdinalIgnoreCase);
                break;
            case FateRankKey:
                settings.FateRanking = ReadAltarPhrases(value);
                break;
            case FateRewardsKey:
                settings.FateRewards = !value.Equals("kapali", StringComparison.OrdinalIgnoreCase);
                break;
            case FateTextKey:
                settings.FatePhrases = ReadAltarPhrases(value);
                break;
            case FateTextColorKey when ReadColor(value) is { } color:
                settings.FateTextColor = color;
                break;
        }
    }

    private void WriteFateLines(List<string> lines)
    {
        lines.Add("");
        lines.Add("# Fate paneli (ayar penceresi, Fates sekmesi): kartlarin ustunde odul yazilari; yazilardan biri gecen odul renkli");
        lines.Add($"{FateRewardsKey} = {(this.FateRewards ? "acik" : "kapali")}");
        lines.Add($"{FateTextColorKey} = {ColorText(this.FateTextColor)}");

        var phrases = this.FatePhrases.Select(p => p.Replace(AltarTextSeparator, ' ').Trim()).Where(p => p.Length > 0);
        lines.Add($"{FateTextKey} = {string.Join($" {AltarTextSeparator} ", phrases)}");

        lines.Add("# Invoke Fates paneli: en iyi kartlar ve tiklama sirasi; odul sirasi en iyi en basta");
        lines.Add($"{FatePlanKey} = {(this.FatePlan ? "acik" : "kapali")}");
        var ranking = this.FateRanking.Select(p => p.Replace(AltarTextSeparator, ' ').Trim()).Where(p => p.Length > 0);
        lines.Add($"{FateRankKey} = {string.Join($" {AltarTextSeparator} ", ranking)}");
    }
}
