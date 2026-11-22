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
///         govde -> 8, + 0x328         KARTIN ODUL OZETI, tek std::wstring: butun oduller alt alta, iki kopya isteyen odulde
///                                     ilerleme ("1/2 ..."). Satirlarin hangi odule ait oldugu ozetten guvenle cikmiyor
///                                     (14.11.2026 Conjurer: "Sceptres dropped by ..." satiri "will drop additional Rare
///                                     Sceptres" satirindan ONCE geliyordu, bolme ucu ayri odul yapti). O yuzden asil kaynak
///                                     GORUNUR simgelerin yazilari (simge basina bir odul, gruplama dogru); bayat yazi
///                                     yalnizca gizli simgelerde goruldu. Gorunur simge yazisi yoksa ozet kullaniliyor.
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
    private const int RowSummaryOffset = 0x330;   // 13.11: 0x328; 19.11.2026: +8 varsayildi (diger alt sinif alanlari gibi), panelde DOGRULANMADI

    private static readonly Regex Tags = new(@"<[^>]*>|[{}]", RegexOptions.CultureInvariant);
    private static readonly Regex Progress = new(@"^[0-9]+/[0-9]+ ", RegexOptions.CultureInvariant);
    private static readonly Regex BlankLines = new(@"\n\s*\n+", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Bir kart: govde ogesi, adi ve odul yazilari (simge sirasiyla). Ekrandaki yeri govde + ad seridi + odul simgeleri
    ///     ogelerinin birlesimi (Parts): 14.11.2026, govdenin kutusu ad seridini ve simge sirasini kapsamiyordu, alttaki kartin
    ///     kutusu ustteki kartin simgelerinin ustune oturdu.
    /// </summary>
    /// <remarks>
    ///     Group: panelde kart yuvasi olan kacinci grup (0 = ustteki secilen kartlar panel/5, 1 = eldeki kartlar panel/6).
    ///     Eldeki sira, uste konmus kartlari da gorunur olarak tutmaya devam ediyor (15.11.2026): elin tamami.
    /// </remarks>
    internal sealed record Card(ulong Body, string Name, IReadOnlyList<string> Rewards, IReadOnlyList<ulong> Parts, int Group = 0);

    /// <summary>Panel "Invoke Fates" paneli mi (kart secme degil, eldeki kartlari uste koyma); baslik ilk cocuklardan birinde.</summary>
    internal static bool IsInvoke(IntPtr h, ulong panel)
    {
        var buf = new byte[TextOffset + 0x20];
        return Read(h, panel, buf) &&
               AltarLabels.Children(h, buf).Take(8).Any(c => TextOf(h, c).Equals("Invoke Fates", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ustteki gruptaki gorunur yuva sayisi: en fazla kac kart konabilir (15.11.2026: 3). Bulunamazsa 0.</summary>
    internal static int TopSlots(IntPtr h, ulong panel) => Slots(h, panel).Count(s => s.Group == 0 && UiFinder.ChainVisible(h, s.Slot));

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

        foreach (var (slot, group) in Slots(h, panel))
        {
            if (ReadCard(h, slot, all) is { } card) { cards.Add(card with { Group = group }); }
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
    private static IEnumerable<(ulong Slot, int Group)> Slots(IntPtr h, ulong panel)
    {
        var buf = new byte[TextOffset + 0x20];
        if (!Read(h, panel, buf)) { yield break; }

        var ordinal = -1;
        foreach (var group in AltarLabels.Children(h, buf).Take(32))
        {
            if (!Read(h, group, buf) || AltarLabels.Children(h, buf) is not [var list, ..]) { continue; }
            if (!Read(h, list, buf)) { continue; }

            var counted = false;
            foreach (var slot in AltarLabels.Children(h, buf).Take(8))
            {
                if (Read(h, slot, buf) && AltarLabels.Children(h, buf) is [_, var help, ..] &&
                    TextOf(h, help).StartsWith(FeatherHelp, StringComparison.Ordinal))
                {
                    if (!counted) { counted = true; ordinal++; }
                    yield return (slot, ordinal);
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

        // Fate'ler secilip alininca panel bos cerceveyle acik kaliyor: kartlarin adi silinip govde gorunur kaliyor, odul
        // simgeleri de eski yazilariyla gorunur (14.11.2026). Adi olmayan kart secilebilir kart degil.
        if (name.Length == 0 && !all) { return null; }

        var shownIcons = new List<string>();
        var hiddenIcons = new List<string>();
        if (Read(h, parts[BodyRewards], buf) && AltarLabels.Children(h, buf) is [var row, ..] && Read(h, row, buf))
        {
            foreach (var icon in AltarLabels.Children(h, buf).Take(8))
            {
                if (!Read(h, icon, buf) || AltarLabels.Children(h, buf) is not { Count: > RewardText } kids) { continue; }

                var text = Clean(TextOf(h, kids[RewardText]));
                if (text.Length == 0) { continue; }

                (UiFinder.Reread(h, icon) is { Visible: true } ? shownIcons : hiddenIcons).Add(text);
            }
        }

        var summary = SplitSummary(TextOf(h, parts[BodyRewards], RowSummaryOffset));
        var shape = new[] { body, parts[BodyName], parts[BodyRewards] };
        if (!all) { return new Card(body, name, shownIcons.Count > 0 ? shownIcons : summary, shape); }

        var rewards = summary.Select(s => "[ozet] " + s)
            .Concat(shownIcons.Select(s => "[simge] " + s))
            .Concat(hiddenIcons.Select(s => "[gizli simge] " + s))
            .ToList();
        return name.Length > 0 || rewards.Count > 0 ? new Card(body, name, rewards, shape) : null;
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

    internal static string TextOf(IntPtr h, ulong element, int offset = TextOffset)
    {
        var buf = new byte[Math.Max(TextOffset, offset) + 0x20];
        return Read(h, element, buf) ? EntityReader.ReadWString(h, buf, offset) : "";
    }

    /// <summary>Ogeyi okur; kendi adresini tutmuyorsa (silinmis ya da baska bir sey) false.</summary>
    internal static bool Read(IntPtr h, ulong address, byte[] buf) =>
        EntityReader.Read(h, address, buf) && BitConverter.ToUInt64(buf, OffSelf) == address;
}
