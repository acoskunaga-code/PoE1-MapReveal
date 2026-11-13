using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Fate secim paneli (Covens ligi): kartlarin altindaki yuvarlak odul simgelerinin yazilari. Oyun her simgenin
///     yazisini panel acilirken gizli bir yazi ogesi olarak hazirliyor, fare uzerine gelince sadece gorunur yapiyor;
///     yazilar o yuzden panel acildigi anda okunabiliyor, uzerine gelmek gerekmiyor.
///
///     PoE1 (14.11.2026, canli, `MapScan uitext Fate` ve `MapScan fates`):
///
///         panel                       kok/1/198/2        (kayitli yol: data\fate-panel.txt)
///         kart listesi                panel/5/0          (5 yuva; kullanilmayanlar gizli. panel/6/0 alttaki 6'li sira)
///         yuva -> 1                   tuy yardim yazisi ("The number of feathers on this icon ..."): yuvanin kimligi
///         yuva -> 0/0                 kart govdesi: cocuk 4 = ad, 5 = aciklama, 8/0 = odul simgeleri (soldan saga)
///         odul simgesi -> 3           yazi ogesi, gizli; yazisi bossa (0x0) o simge kullanilmiyor
///         odul simgesinin gorunurlugu  KULLANILMAYAN simge gizli ama yazisi onceki panelden kaliyor (14.11.2026: tek
///                                     simgeli Black Frost kartinda bir onceki kartin "Rare Wands" yazisi duruyordu);
///                                     o yuzden yalnizca kendi gorunur biti acik simgelerin yazisi aliniyor
///         yazi                        oge + AltarLabels.TextOffset (0x358), std::wstring
///         govde -> 8, + 0x328         KARTIN ODUL OZETI, tek std::wstring: butun oduller alt alta, "Empowered Monster ..."
///                                     ile baslayan satir yeni odul, digerleri onceki odulun devami; iki kopya isteyen
///                                     odulde ilerleme ("1/2 ..."). Asil kaynak bu: simgelerin yazilari bayat kalabiliyor
///                                     (00:58 dokumu: Wildfire ozeti tek odul, simgelerde iki yazi vardi). Ozet bossa
///                                     gorunur simgelerin yazilari kullaniliyor.
///
///     Panel ilk kez tuy yardim yazisi aranarak bulunuyor (yazi -> yuva -> liste -> grup -> panel, dort ebeveyn yukari)
///     ve yolu kaydediliyor; sonraki acilislarda aramasiz. Yamadan sonra yol tutmazsa overlay kendisi yeniden arar.
/// </summary>
internal static class FateLabels
{
    internal const string FeatherHelp = "The number of feathers on this icon";

    private const int TextOffset = AltarLabels.TextOffset;
    private const int OffSelf = 0xB8;
    private const int BodyName = 4;
    private const int BodyRewards = 8;
    private const int RewardText = 3;
    private const int RowSummaryOffset = 0x328;

    private static readonly Regex Tags = new(@"<[^>]*>|[{}]", RegexOptions.CultureInvariant);
    private static readonly Regex Progress = new(@"^[0-9]+/[0-9]+ ", RegexOptions.CultureInvariant);
    private static readonly Regex BlankLines = new(@"\n\s*\n+", RegexOptions.CultureInvariant);

    /// <summary>Bir kart: govde ogesi (ekrandaki yeri icin), adi ve odul yazilari (simge sirasiyla).</summary>
    internal sealed record Card(ulong Body, string Name, IReadOnlyList<string> Rewards);

    internal static string PathFile => AppPaths.File("fate-panel.txt");

    /// <summary>Yavas arama (arka planda): kokten tuy yardim yazisini bulup paneli dondurur. Yoksa 0.</summary>
    internal static ulong FindPanel(IntPtr h, ulong root, int maxDepth = 7, int maxElements = 80_000)
    {
        var queue = new Queue<(ulong Address, int Depth)>();
        var seen = new HashSet<ulong> { root };
        queue.Enqueue((root, 0));
        var buf = new byte[TextOffset + 0x20];

        while (queue.Count > 0 && seen.Count < maxElements)
        {
            var (address, depth) = queue.Dequeue();
            if (!Read(h, address, buf)) { continue; }

            if (EntityReader.ReadWString(h, buf, TextOffset).StartsWith(FeatherHelp, StringComparison.Ordinal))
            {
                var panel = AltarLabels.Ancestor(h, address, 4);
                if (panel != 0 && LooksLikePanel(h, panel)) { return panel; }
            }

            if (depth >= maxDepth) { continue; }

            foreach (var child in AltarLabels.Children(h, buf))
            {
                if (seen.Add(child)) { queue.Enqueue((child, depth + 1)); }
            }
        }

        return 0;
    }

    /// <summary>Ogede en az bir kart yuvasi var mi (gorunurlukten bagimsiz). Kayitli yoldan gelen ogeyi sinamak icin.</summary>
    internal static bool LooksLikePanel(IntPtr h, ulong panel) => Slots(h, panel).Any();

    /// <summary>
    ///     Hizli tarama (saniyede birkac kez): panel gorunurse gorunur kartlarin adi ve odul yazilari. Panel kapaliysa
    ///     bos liste; oge artik gecerli degilse (kendi adresini tutmuyor) null: yeniden bulunmali.
    /// </summary>
    /// <remarks><paramref name="all"/>: tani icin (MapScan fates hepsi) gizli panel, kart ve simgeler de; gizli odul "[gizli] " ile.</remarks>
    internal static List<Card>? Scan(IntPtr h, ulong panel, bool all = false)
    {
        var buf = new byte[TextOffset + 0x20];
        if (!Read(h, panel, buf)) { return null; }

        var cards = new List<Card>();
        if (!all && !UiFinder.ChainVisible(h, panel)) { return cards; }

        foreach (var slot in Slots(h, panel))
        {
            if (ReadCard(h, slot, all) is { } card) { cards.Add(card); }
        }

        return cards;
    }

    /// <summary>Oyunun bicim etiketleri atiliyor, "Empowered Monster" kisaltiliyor, bos satirlar teke iniyor.</summary>
    internal static string Clean(string text)
    {
        text = Tags.Replace(text.Replace("\r", ""), "").Replace("Empowered Monster", "Monster");
        return BlankLines.Replace(text, "\n").Trim();
    }

    /// <summary>Panelin kart yuvalari: panel/grup/0/yuva, ikinci cocugu tuy yardim yazisi olanlar.</summary>
    private static IEnumerable<ulong> Slots(IntPtr h, ulong panel)
    {
        var buf = new byte[TextOffset + 0x20];
        if (!Read(h, panel, buf)) { yield break; }

        foreach (var group in AltarLabels.Children(h, buf).Take(32))
        {
            if (!Read(h, group, buf) || AltarLabels.Children(h, buf) is not [var list, ..]) { continue; }
            if (!Read(h, list, buf)) { continue; }

            foreach (var slot in AltarLabels.Children(h, buf).Take(8))
            {
                if (Read(h, slot, buf) && AltarLabels.Children(h, buf) is [_, var help, ..] &&
                    TextOf(h, help).StartsWith(FeatherHelp, StringComparison.Ordinal))
                {
                    yield return slot;
                }
            }
        }
    }

    private static Card? ReadCard(IntPtr h, ulong slot, bool all)
    {
        var buf = new byte[TextOffset + 0x20];
        if (!Read(h, slot, buf) || AltarLabels.Children(h, buf) is not [var holder, ..]) { return null; }
        if (!Read(h, holder, buf) || AltarLabels.Children(h, buf) is not [var body, ..]) { return null; }
        if (!Read(h, body, buf) || (!all && !UiFinder.ChainVisible(h, body))) { return null; }

        var parts = AltarLabels.Children(h, buf);
        if (parts.Count <= BodyRewards) { return null; }

        var name = Clean(TextOf(h, parts[BodyName]));
        var rewards = SplitSummary(TextOf(h, parts[BodyRewards], RowSummaryOffset));
        if (rewards.Count > 0 && !all) { return name.Length > 0 ? new Card(body, name, rewards) : null; }

        if (Read(h, parts[BodyRewards], buf) && AltarLabels.Children(h, buf) is [var row, ..] && Read(h, row, buf))
        {
            foreach (var icon in AltarLabels.Children(h, buf).Take(8))
            {
                if (!Read(h, icon, buf) || AltarLabels.Children(h, buf) is not { Count: > RewardText } kids) { continue; }

                var shown = UiFinder.Reread(h, icon) is { Visible: true };
                if (!shown && !all) { continue; }

                var text = Clean(TextOf(h, kids[RewardText]));
                if (text.Length > 0) { rewards.Add(all ? (shown ? "[simge] " : "[gizli simge] ") + text : text); }
            }
        }

        // Fate'ler secilip alininca panel bos cerceveyle acik kaliyor: kartlarin adi silinip govde gorunur kaliyor, odul
        // simgeleri de eski yazilariyla gorunur (14.11.2026). Adi olmayan kart secilebilir kart degil.
        if (name.Length == 0 && !all) { return null; }

        return name.Length > 0 || rewards.Count > 0 ? new Card(body, name, rewards) : null;
    }

    /// <summary>Odul ozetini odullere boler: "Empowered Monster" (ya da "1/2 ...") ile baslayan satir yeni odul.</summary>
    internal static List<string> SplitSummary(string summary)
    {
        var rewards = new List<string>();
        foreach (var raw in summary.Replace("\r", "").Split('\n'))
        {
            var line = Clean(raw);
            if (line.Length == 0) { continue; }

            var starts = Tags.Replace(raw, "").TrimStart().StartsWith("Empowered Monster", StringComparison.Ordinal) ||
                         Progress.IsMatch(line);
            if (starts || rewards.Count == 0) { rewards.Add(line); }
            else { rewards[^1] += "\n" + line; }
        }

        return rewards;
    }

    private static string TextOf(IntPtr h, ulong element, int offset = TextOffset)
    {
        var buf = new byte[Math.Max(TextOffset, offset) + 0x20];
        return Read(h, element, buf) ? EntityReader.ReadWString(h, buf, offset) : "";
    }

    /// <summary>Ogeyi okur; kendi adresini tutmuyorsa (silinmis ya da baska bir sey) false.</summary>
    private static bool Read(IntPtr h, ulong address, byte[] buf) =>
        EntityReader.Read(h, address, buf) && BitConverter.ToUInt64(buf, OffSelf) == address;
}
