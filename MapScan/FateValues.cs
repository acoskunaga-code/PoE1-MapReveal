using System.Globalization;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Fate odulunun degeri, chaos olarak: "yazi = deger" satirlari, odul yazisinda gecen ILK satir gecerli. Sondaki
///     [2] / [3] satiri yalnizca o kadar kopya isteyen odule uygular ("Currency Items [2]").
///
///     Fiyatlar 22.11.2026 poe.ninja, Covens ligi (Divine 214c, Hinekora's Lock 15231c, Mirror Shard 6119c, Fracturing
///     Orb 1458c, Reflecting Mist 721c, Stacked Deck 3.75c, Thread ~1c, T16 harita 1c). Oyun odul basina kac esya
///     dustugunu ve "Currency Items" havuzunu yayinlamiyor: o satirlar, rarity ve rare esyalar TAHMIN.
///
///     Alan seviyesi sinavi yok: odul tablosundaki seviye araligi (AuguryRewardOutcomes +0x18 / +0x1C) odulun teklif
///     edilmesini belirliyor, karttaki odul zaten o alanda cikabilen oduldur.
/// </summary>
internal sealed class FateValues
{
    /// <summary>Hicbir satira uymayan odul.</summary>
    private const double Unknown = 1;

    internal static readonly string[] Defaults =
    {
        "Hinekora's Lock = 15200",
        "Mirror Shard = 6100",
        "Fracturing Orb = 1450",
        "Reflecting Mist = 720",
        "Divine Orbs = 428",
        "Divine Orb = 214",
        "Valuable Currency Items = 250",
        "Currency Items [2] = 50",
        "Currency Items = 20",
        "Sacred Orb = 18",
        "Orbs of Annulment = 14",
        "Orb of Annulment = 7",
        "Unique Jewel [3] = 60",
        "Valuable Unique = 30",
        "Dreadmaidens' Sacrament = 1",
        "Hide of the Vendigo = 1",
        "Corpse Rose = 1",
        "Ordered Fate = 1",
        "Doedre's Tongue = 1",
        "extremely increased Item Rarity = 40",
        "massively increased Item Rarity = 25",
        "greatly increased Item Rarity = 10",
        "increased Item Rarity = 5",
        "Viscera = 15",
        "Exceptional Gem = 15",
        "maximum Quality = 8",
        "many additional Stacked Decks = 18",
        "Stacked Decks = 8",
        "many additional Threads = 6",
        "Threads = 2",
        "many additional Rare Maps = 5",
        "Rare Map = 2",
        "Unique = 4",
        "Amulets dropped by Monster will have a Gem modifier = 30",
        "Gems = 1",
        "Gold = 0",
        "Rare = 0.5",
    };

    private static readonly Regex Line = new(@"^(.+?)\s*(?:\[([1-3])\])?\s*=\s*(\d+(?:[.,]\d+)?)$", RegexOptions.CultureInvariant);

    private readonly List<(string Phrase, int Need, double Value)> entries = new();

    internal static FateValues Default { get; } = new(Defaults);

    internal FateValues(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            if (Line.Match(raw.Trim()) is not { Success: true } m) { continue; }

            var phrase = AltarLabels.Normalize(FateLabels.Clean(m.Groups[1].Value));
            if (phrase.Length < 3) { continue; }

            var need = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (double.TryParse(m.Groups[3].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                this.entries.Add((phrase, need, value));
            }
        }
    }

    internal double Of(string text, int need)
    {
        var normal = AltarLabels.Normalize(text);
        foreach (var (phrase, wanted, value) in this.entries)
        {
            if ((wanted == 0 || wanted == need) && normal.Contains(phrase, StringComparison.OrdinalIgnoreCase)) { return value; }
        }

        return Unknown;
    }
}
