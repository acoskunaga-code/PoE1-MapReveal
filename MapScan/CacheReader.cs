namespace Poe2Map;

/// <summary>
///     Djinn's Cache (3.30 Faridun sandiklari): haritada isaret + ad. Oyunun harita simgesi (MinimapIcon) YOK, o yuzden
///     Icons sekmesinde degiller; yoldan taniniyorlar. 14.11.2026 canli (`MapScan paths "Faridun"`):
///
///         Metadata/Chests/Faridun/MinorCacheBronzeCurrency   "Bronze Djinn's Cache"
///         Metadata/Chests/Faridun/MinorCacheBronzeWeapons    "Bronze Djinn's Cache"
///         Metadata/Chests/Faridun/MinorCacheSilverWeapons    "Silver Djinn's Cache"
///
///     Yolun sonu: Minor/Major + maden + icerik. Etiket oyundaki ad (Render +0x148) + icerik: "Silver Djinn's Cache:
///     Weapons". Bilesenler: Chest, Targetable, StateMachine, InteractionAction (MinimapIcon yok).
/// </summary>
internal static class CacheReader
{
    internal enum Metal { None, Bronze, Silver, Gold, Other }

    internal sealed record Marker(Metal Metal, string Label);

    internal static readonly Marker NotCache = new(Metal.None, "");

    private const string Folder = "Metadata/Chests/Faridun/";

    /// <summary>Varligin sandik isareti; Djinn's Cache degilse <see cref="NotCache"/>.</summary>
    internal static Marker Classify(IntPtr h, EntityReader.EntityInfo info)
    {
        var path = info.Path;
        if (!path.StartsWith(Folder, StringComparison.Ordinal) || !path.Contains("Cache", StringComparison.Ordinal)) { return NotCache; }

        var tail = path[(path.LastIndexOf('/') + 1)..];
        var at = tail.IndexOf('@');
        if (at >= 0) { tail = tail[..at]; }

        var metal = Metal.Other;
        var contents = "";
        foreach (var m in new[] { Metal.Bronze, Metal.Silver, Metal.Gold })
        {
            var i = tail.IndexOf(m.ToString(), StringComparison.Ordinal);
            if (i < 0) { continue; }

            metal = m;
            contents = Spaced(tail[(i + m.ToString().Length)..]);
            break;
        }

        var name = MapIconReader.DisplayName(h, info);
        if (name.Length == 0) { name = metal is Metal.Other ? "Djinn's Cache" : $"{metal} Djinn's Cache"; }

        return new Marker(metal, contents.Length > 0 ? $"{name}: {contents}" : name);
    }

    /// <summary>"BodyArmours" -> "Body Armours".</summary>
    private static string Spaced(string camel)
    {
        var sb = new System.Text.StringBuilder(camel.Length + 4);
        for (var i = 0; i < camel.Length; i++)
        {
            if (i > 0 && char.IsUpper(camel[i]) && !char.IsUpper(camel[i - 1])) { sb.Append(' '); }
            sb.Append(camel[i]);
        }

        return sb.ToString();
    }
}
