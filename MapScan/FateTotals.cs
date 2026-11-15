namespace Poe2Map;

/// <summary>
///     Fate panelinin secim satiri: ustteki yuvalara konan kartlarin TOPLAM odulleri, yuvalarin hemen altindaki simge
///     sirasi. Ayni addan iki (ya da uc) kart konunca "invoke two/three copies" odulleri burada acilir.
///
///     PoE1 (15.11.2026, canli, `MapScan fates`; iki Torchbearer ustte):
///
///         satir                       panel/7            (cocuk 0 = kazanilan oduller, 1 = kopya odulleri, 2 = ozet yazisi)
///         panel/7/0/j, panel/7/1/j    odul simgesi; -> 3 yazi ogesi (+0x358), kart simgeleriyle ayni yapi
///         panel/7/2 + 0x358           ozet: "<valuedefault>{3x ...}" kazanilan, "<default>{1/2 ...}" eksik kopya
///
///     Kart simgelerinde oldugu gibi kullanilmayan simge gizli ama eski yazisini tutuyor olabilir; Visible ile ayriliyor.
/// </summary>
internal static class FateTotals
{
    private const int RowIndex = 7;
    private const int SummaryIndex = 2;
    private const int RewardText = 3;

    internal sealed record Icon(ulong Element, string Text, bool Visible);

    internal sealed record Row(ulong Element, IReadOnlyList<Icon> Gains, IReadOnlyList<Icon> Copies, string Summary);

    /// <summary>Secim satiri; panelde yoksa ya da yapisi tutmuyorsa null.</summary>
    internal static Row? Read(IntPtr h, ulong panel)
    {
        var buf = new byte[AltarLabels.TextOffset + 0x20];
        if (!FateLabels.Read(h, panel, buf) || AltarLabels.Children(h, buf) is not { Count: > RowIndex } groups) { return null; }

        var row = groups[RowIndex];
        if (!FateLabels.Read(h, row, buf) || AltarLabels.Children(h, buf) is not { Count: > SummaryIndex } parts) { return null; }

        return new Row(row, Icons(h, parts[0]), Icons(h, parts[1]), FateLabels.TextOf(h, parts[SummaryIndex]));
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
