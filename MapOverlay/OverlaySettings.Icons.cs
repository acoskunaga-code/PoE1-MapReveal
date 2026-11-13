namespace Poe2Map;

/// <summary>
///     Harita simgelerinin ayari (ayar penceresindeki "Icons" sekmesi): simge adi basina cap, piksel.
///     Dosyada "icon.&lt;ad&gt; = 24" satiri; 0 ya da satir yok = kapali. "icon-names = acik / kapali" simgenin
///     yaninda oyundaki adi (shrine / strongbox turu, NPC adi) yazar; varsayilan acik.
/// </summary>
internal sealed partial class OverlaySettings
{
    internal const int IconMaxSize = 64;

    private const string IconNamesKey = "icon-names";

    /// <summary>Simge adi -> cap (piksel). Kapali simge listede yok. Ad buyuk/kucuk harfe duyarsiz.</summary>
    internal Dictionary<string, int> IconSizes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Simgelerin yaninda adlari yazilsin mi.</summary>
    internal bool IconNames { get; set; } = true;

    /// <summary>"icon.abysspillar = 24" satirinin "abysspillar" ve "24" parcalari (anahtar Load'da kucuk harfe cevriliyor).</summary>
    private static void ReadIconLine(OverlaySettings settings, string name, string value)
    {
        if (name.Length == 0 || !int.TryParse(value, out var size)) { return; }

        size = Math.Clamp(size, 0, IconMaxSize);
        if (size > 0) { settings.IconSizes[name] = size; }
    }

    private void WriteIconLines(List<string> lines)
    {
        lines.Add("");
        lines.Add("# Harita simgeleri (ayar penceresi, Icons sekmesi): icon.<ad> = cap (piksel), 0 = kapali");
        lines.Add($"{IconNamesKey} = {(this.IconNames ? "acik" : "kapali")}");
        foreach (var (name, size) in this.IconSizes.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (size > 0) { lines.Add($"icon.{name} = {size}"); }
        }
    }
}
