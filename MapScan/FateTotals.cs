namespace Poe2Map;

/// <summary>
///     Fate panelinin secim satiri: ustteki yuvalara konan kartlarin TOPLAM odulleri, yuvalarin hemen altindaki simge
///     sirasi. Ayni addan iki (ya da uc) kart konunca "invoke two/three copies" odulleri burada acilir.
///
///     PoE1 (15.11.2026, canli, `MapScan fates`; iki Torchbearer ustte):
///
///         satir                       panelin uc cocuklu tek cocugu: 15.11 panel/7, 22.11 panel/8 (3.30.0d araya bir oge
///                                     ekledi), o yuzden sirayla degil yapiyla bulunuyor
///                                     (cocuk 0 = kazanilan oduller, 1 = kopya odulleri, 2 = ozet yazisi)
///         satir/0/j, satir/1/j        odul simgesi; -> 3 yazi ogesi (TextOffset), kart simgeleriyle ayni yapi
///         satir/2 + TextOffset        ozet: "<valuedefault>{3x ...}" kazanilan, "<default>{1/2 ...}" eksik kopya
///
///     Kart simgelerinde oldugu gibi kullanilmayan simge gizli ama eski yazisini tutuyor olabilir; Visible ile ayriliyor.
/// </summary>
internal static class FateTotals
{
    private const int RowParts = 3;
    private const int SummaryIndex = 2;
    private const int RewardText = 3;

    internal sealed record Icon(ulong Element, string Text, bool Visible);

    internal sealed record Row(ulong Element, IReadOnlyList<Icon> Gains, IReadOnlyList<Icon> Copies, string Summary);

    /// <summary>Secim satiri; panelde yoksa ya da yapisi tutmuyorsa null.</summary>
    internal static Row? Read(IntPtr h, ulong panel)
    {
        var buf = new byte[AltarLabels.TextOffset + 0x20];
        if (!FateLabels.Read(h, panel, buf)) { return null; }

        foreach (var child in AltarLabels.Children(h, buf).Take(32))
        {
            if (!FateLabels.Read(h, child, buf) || AltarLabels.Children(h, buf) is not { Count: RowParts } parts) { continue; }

            return new Row(child, Icons(h, parts[0]), Icons(h, parts[1]), FateLabels.TextOf(h, parts[SummaryIndex]));
        }

        return null;
    }

    private static List<Icon> Icons(IntPtr h, ulong list)
    {
        var icons = new List<Icon>();
        var buf = new byte[AltarLabels.TextOffset + 0x20];
        if (!FateLabels.Read(h, list, buf)) { return icons; }

        foreach (var icon in AltarLabels.Children(h, buf).Take(16))
        {
            if (!FateLabels.Read(h, icon, buf) || AltarLabels.Children(h, buf) is not { Count: > RewardText } kids) { continue; }

            var text = FateLabels.Clean(FateLabels.TextOf(h, kids[RewardText]));
            icons.Add(new Icon(icon, text, UiFinder.Reread(h, icon) is { Visible: true }));
        }

        return icons;
    }
}
