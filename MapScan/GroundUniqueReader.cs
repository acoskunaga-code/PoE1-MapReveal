namespace Poe2Map;

/// <summary>
///     Yerdeki unique esyanin adi, tanimlanmamis olsa bile. 18.11.2026 canli (`MapScan grounditems`, 204 yer esyasi):
///
///         WorldItem varligi (Metadata/MiscellaneousObjects/WorldItem) WorldItem bileseni +0x28 -> esya varligi
///         esya Mods bileseni +0xB4 (int) nadirlik: 0 normal, 1 magic, 2 rare, 3 unique (beyaz esyalarda 0, unique'lerde 3)
///         esya RenderItem bileseni +0x28 -> resim yolu (UTF-16), unique'te unique'in kendi resmi:
///             Belt5 -> Belts/LinkmastersCoil.dds, Ring13 -> Rings/BereksGrip.dds, BodyInt14 -> BodyArmours/VisMortis.dds,
///             HelmetDexInt5 -> Helmets/ThreeDragons.dds; ayni tabandaki rare/beyaz esyada taban resmi (Belts/Belt4.dds).
///
///     Ad once istege bagli data\uniques.json'dan (RePoE uniques.json: ad + resim; UniqueArt.cs, data/README.md),
///     yoksa resim dosyasinin adindan: "LinkmastersCoil" -> "Linkmasters Coil" (kesme isareti ve "The" yok). Resim adi
///     unique'in adi olmayabiliyor: Mageblood = Belts/InjectorBelt.dds (18.11.2026 saticidaki Mageblood bu resmi yukledi),
///     Meginord's Girdle = Belts/Belt7Unique.dds; bunlar KnownArt'ta.
/// </summary>
internal static class GroundUniqueReader
{
    internal static ulong WorldItemEntity = 0x28;
    internal static ulong ModsRarity = 0xB4;
    internal static ulong RenderItemArt = 0x28;
    private const int UniqueRarity = 3;

    /// <summary>Unique yer esyasinin adi; yer esyasi degilse ya da unique degilse "", henuz okunamadiysa null.</summary>
    internal static string? Classify(IntPtr h, EntityReader.EntityInfo info)
    {
        // Yer esyasi degilse kesin "". Yer esyasinin WorldItem bileseni yoksa varlik yarim okunmus (yeni dusmus): null,
        // yeniden denensin (18.11.2026: envantere alinip geri atilan esyanin adi hic cikmiyordu).
        if (!info.Path.EndsWith("/WorldItem", StringComparison.Ordinal)) { return ""; }
        if (!info.Components.TryGetValue("WorldItem", out var world)) { return null; }

        // Esya varligi dunya listesinde degil; +0x8C bayti onda bayrak degil (21.11.2026 `MapScan itemflags`: 2260 yer
        // esyasinin ~800'unde bit 0 acik, deger zamanla degisiyor). Bayrak sinamasi esyanin nadirligini 0.5-5 sn
        // geciktiriyor ya da hic okutmuyordu (kullanici: "esyalari gec ciziyor belirginlestiriyor"); burada yok sayiliyor.
        var item = PlayerChain.ReadPointer(h, world + WorldItemEntity);
        if (EntityReader.ReadInfo(h, item, anyFlag: true) is not { } it) { return null; }
        if (!it.Components.TryGetValue("Mods", out var mods) || !it.Components.TryGetValue("RenderItem", out var render)) { return ""; }

        var rarity = new byte[4];
        if (!EntityReader.Read(h, mods + ModsRarity, rarity)) { return null; }
        if (BitConverter.ToInt32(rarity, 0) != UniqueRarity) { return ""; }

        if (ArtPath(h, PlayerChain.ReadPointer(h, render + RenderItemArt)) is not { } art) { return null; }

        return UniqueArt.Names(art) ?? NameFromArt(art);
    }

    private static string? ArtPath(IntPtr h, ulong at)
    {
        var raw = new byte[0x100];
        if (!PlayerChain.LooksLikePointer(at) || !EntityReader.Read(h, at, raw)) { return null; }

        var chars = new List<char>();
        for (var i = 0; i + 1 < raw.Length; i += 2)
        {
            var ch = (char)BitConverter.ToUInt16(raw, i);
            if (ch == 0) { break; }
            if (ch < 0x20 || ch > 0x7E) { return null; }
            chars.Add(ch);
        }

        var path = new string(chars.ToArray());
        return path.StartsWith("Art/", StringComparison.Ordinal) ? path : null;
    }

    /// <summary>
    ///     uniques.json yokken de dogru cikmasi gereken, resim adi unique'in adi olmayanlar. Belt7Unique: kullanici
    ///     18.11.2026. InjectorBelt: 18.11.2026 saticidaki tanimli Mageblood ekrandayken bellekte yuklenen esya resmi
    ///     (`MapScan strscan Belts/InjectorBelt`, "|0,0,0,0|0" arayuz dokusu kayitlari); RePoE de ayni.
    /// </summary>
    private static readonly Dictionary<string, string> KnownArt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Belt7Unique"] = "Meginord's Girdle",
        ["InjectorBelt"] = "Mageblood",
    };

    /// <summary>"Art/2DItems/Belts/LinkmastersCoil.dds" -> "Linkmasters Coil".</summary>
    internal static string NameFromArt(string art)
    {
        var slash = art.LastIndexOf('/');
        var file = slash >= 0 ? art[(slash + 1)..] : art;
        var dot = file.LastIndexOf('.');
        if (dot > 0) { file = file[..dot]; }
        if (KnownArt.TryGetValue(file, out var known)) { return known; }

        // Yenilenmis resimlerin sonundaki sayi: "Headhunter2" -> "Headhunter" (18.11.2026 bellekte tek Headhunter esya
        // resmi Art/2DItems/Belts/Headhunter2.dds; ArchnemesisHeadhunter.dds bir MTX gorunumu).
        var bare = file.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (bare.Length >= 4) { file = bare; }

        var name = new System.Text.StringBuilder(file.Length + 8);
        for (var i = 0; i < file.Length; i++)
        {
            var c = file[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(file[i - 1]) || (i + 1 < file.Length && char.IsLower(file[i + 1]) && char.IsUpper(file[i - 1]))))
            {
                name.Append(' ');
            }

            name.Append(c == '_' ? ' ' : c);
        }

        return name.ToString().Trim();
    }
}
