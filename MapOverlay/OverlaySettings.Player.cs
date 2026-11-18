namespace Poe2Map;

/// <summary>
///     "oyuncu-isareti = acik / kapali": haritada oyuncunun yerine kirmizi halka ve arti. Varsayilan kapali (kullanici
///     18.11.2026: "bu isareti kaldirabilirmiyiz"); General sekmesindeki kutudan acilabiliyor.
/// </summary>
internal sealed partial class OverlaySettings
{
    private const string PlayerMarkerKey = "oyuncu-isareti";

    internal bool PlayerMarker { get; set; }

    private void WritePlayerLines(List<string> lines)
    {
        lines.Add("");
        lines.Add("# Haritada oyuncunun yerine kirmizi halka ve arti");
        lines.Add($"{PlayerMarkerKey} = {(this.PlayerMarker ? "acik" : "kapali")}");
    }
}
