namespace Poe2Map;

/// <summary>
///     Kisayol tuslari (ayar penceresi, General sekmesi; <see cref="GlobalHotKey"/>). Deger Keys adi:
///
///         onoff-tusu = F9            overlay'i tamamen gizler / geri getirir
///         yol-tusu   = F10           cikis yollarini (cizgiler, kapi isaretleri ve adlari) gizler / geri getirir
///
///     "None" = tus yok. Ctrl / Alt / Shift ile: "F9, Control". Yollar her acilista acik baslar; tus sadece o
///     oturum icin gizliyor.
/// </summary>
internal sealed partial class OverlaySettings
{
    private const string ToggleKeyName = "onoff-tusu";
    private const string PathKeyName = "yol-tusu";

    internal Keys ToggleKey { get; set; } = Keys.F9;

    internal Keys PathKey { get; set; } = Keys.F10;

    private static Keys? ReadKey(string value) =>
        Enum.TryParse<Keys>(value, ignoreCase: true, out var key) ? key : null;

    private void WriteKeyLines(List<string> lines)
    {
        lines.Add("");
        lines.Add("# Kisayollar (ayar penceresi, General sekmesi): overlay acma/kapama ve cikis yollari");
        lines.Add($"{ToggleKeyName} = {this.ToggleKey}");
        lines.Add($"{PathKeyName} = {this.PathKey}");
    }
}
