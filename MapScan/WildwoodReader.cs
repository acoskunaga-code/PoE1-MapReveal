using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     The Viridian Wildwood (Azmeri) nesneleri: uc renk wisp, isik / yakit kureleri ve Funerary altarlari.
///
///     PoE1 (12.11.2026, canli, MapScan paths / entityrefs):
///
///         Metadata/MiscellaneousObjects/Azmeri/AzmeriResourceBase        wisp (192 tane, hepsi ayni yol)
///         Metadata/MiscellaneousObjects/Azmeri/AzmeriResourcePrimalist*  ozel wisp (5 tane, primal_big)
///         Metadata/MiscellaneousObjects/Azmeri/AzmeriFuelResupply        kesif buff'i veren kure ("yakit")
///         Metadata/MiscellaneousObjects/Azmeri/AzmeriBuffEffigySmall     "Nameless Shrine" (kullanicinin verdigi ad)
///         Animated bileseni +0x260 -> animasyon varligi, yolu turu ve boyutu soyluyor:
///             .../wisp_doodads/wisp_primal_{sml,med,big}.ao   Primal (mavi)
///             .../wisp_doodads/wisp_warden_{sml,med,big}.ao   Vivid  (sari; Warden of Eaves'in wisp'i)
///             .../wisp_doodads/wisp_vodoo_{sml,med,big}.ao    Wild   (mor; Warlock of the Mists'in wisp'i)
///
///     Renk eslesmesi poedb'den (Vivid / Wild / Primal ve tuccarlari). Altarlarin ve isik kuresinin yolu henuz
///     gorulmedi: Azmeri klasorundeki tanimadigimiz her nesne adiyla (Render +0x148) "diger" olarak ciziliyor ve
///     gunluge yolu yaziliyor. Yeniden bulmak: `MapScan paths Azmeri`, `MapScan entityrefs AzmeriResource`.
/// </summary>
internal static class WildwoodReader
{
    /// <summary>Animated bileseninde animasyon varligina giden isaretci.</summary>
    internal static ulong AnimatedObject = 0x260;

    private static readonly Regex Wisp = new(@"wisp_(?<kind>primal|warden|vodoo)_(?<size>sml|med|big)", RegexOptions.IgnoreCase);

    internal enum Kind { None, Primal, Vivid, Wild, Fuel, Altar, NamelessShrine, Other }

    // AzmeriBuffEffigy*: kure degil, ayri bir nesne (kullanici, 12.11.2026); ekranda "Nameless Shrine" yazilsin.
    private const string NamelessShrineLabel = "Nameless Shrine";

    /// <summary>Isaret: tur, boyut (wisp: 1 kucuk, 2 orta, 3 buyuk) ve yanina yazilacak ad.</summary>
    internal sealed record Marker(Kind Kind, int Size, string Label);

    internal static readonly Marker NotWildwood = new(Kind.None, 0, "");

    /// <summary>
    ///     Varligin Wildwood isareti; Wildwood nesnesi degilse <see cref="NotWildwood"/>. Animasyon varligi henuz
    ///     yuklenmemis bir wisp icin null: sonraki turda yeniden sorulmali.
    /// </summary>
    internal static Marker? Classify(IntPtr h, EntityReader.EntityInfo info)
    {
        var path = info.Path;
        if (!path.Contains("Azmeri", StringComparison.Ordinal) ||
            path.Contains("/Monsters/", StringComparison.Ordinal) ||
            path.Contains("/NPC/", StringComparison.Ordinal) ||
            path.Contains("/Effects/", StringComparison.Ordinal) ||
            path.Contains("/Characters/", StringComparison.Ordinal))
        {
            return NotWildwood;
        }

        if (path.Contains("AzmeriResource", StringComparison.Ordinal))
        {
            if (!info.Components.TryGetValue("Animated", out var animated)) { return NotWildwood; }

            var ao = PlayerChain.ReadPointer(h, animated + AnimatedObject);
            if (EntityReader.ReadInfo(h, ao) is not { } aoInfo) { return null; }

            var m = Wisp.Match(aoInfo.Path);
            if (!m.Success) { return new Marker(Kind.Other, 0, Tail(aoInfo.Path)); }

            var kind = m.Groups["kind"].Value.ToLowerInvariant() switch
            {
                "primal" => Kind.Primal,
                "warden" => Kind.Vivid,
                _ => Kind.Wild,
            };
            var size = m.Groups["size"].Value.ToLowerInvariant() switch { "big" => 3, "med" => 2, _ => 1 };
            return new Marker(kind, size, "");
        }

        if (path.Contains("BuffEffigy", StringComparison.Ordinal)) { return new Marker(Kind.NamelessShrine, 0, NamelessShrineLabel); }

        var name = MapIconReader.DisplayName(h, info);
        if (path.Contains("Fuel", StringComparison.Ordinal)) { return new Marker(Kind.Fuel, 0, name.Length > 0 ? name : "Fuel"); }
        if (path.Contains("Altar", StringComparison.Ordinal)) { return new Marker(Kind.Altar, 0, name.Length > 0 ? name : Tail(path)); }

        return new Marker(Kind.Other, 0, name.Length > 0 ? name : Tail(path));
    }

    /// <summary>"Metadata/.../AzmeriFuelResupply" -> "AzmeriFuelResupply".</summary>
    private static string Tail(string path)
    {
        var slash = path.LastIndexOf('/');
        var tail = slash >= 0 ? path[(slash + 1)..] : path;
        return tail.EndsWith(".ao", StringComparison.Ordinal) ? tail[..^3] : tail;
    }
}
