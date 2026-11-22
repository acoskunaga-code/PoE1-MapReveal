using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     "Select a Fate" paneli: her kazanda teklif edilen kartlardan hangisinin alinacagi (FatePlanner.Draft). Onerilen kartin
///     ortasinda yesil "TAKE" etiketi ve cerceve; yazi kutusu yok.
///
///     Panel (16.11.2026 canli, `MapScan fates`): ilk secimden once yalniz teklif (grup 0, 3 kart); secimden sonra ayni
///     panelde yeni teklif (grup 0, 4 kart) ve altinda el (grup 1). Invoke Fates basligi yok (FateLabels.IsInvoke false).
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly Pen CopyFrame = new(Color.FromArgb(255, 190, 40), 4f);
    private static readonly SolidBrush CopyBack = new(Color.FromArgb(240, 200, 130, 10));
    private static readonly SolidBrush CopyText = new(Color.FromArgb(20, 12, 0));

    // Select a Fate: eldeki kart adlari -> kac tane (teklifte ayni adli kartlar sari cerceveyle belirginlesiyor).
    private Dictionary<string, int> fateHandNames = new(StringComparer.OrdinalIgnoreCase);

    private (FateLabels.Card Pick, FatePlanner.Plan Plan)? Draft(IReadOnlyList<FateLabels.Card> cards, List<FateLabels.Card> hand)
    {
        var offers = cards.Where(c => c.Group == 0).ToList();
        this.fateHandNames = offers.Count == 0
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : hand.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return offers.Count == 0 ? null : FatePlanner.Draft(offers, hand, this.invokeSlots, this.fateValues);
    }

    /// <summary>
    ///     Teklifteki kartlardan elde AYNI ADLI olani olanlar: kalin sari cerceve ve kartin ustunde "COPY - n in hand"
    ///     (kullanicinin istegi 16.11.2026: secilmemis ustteki kartlardan eldekilerle ayni isimli olanlar belirgin olsun).
    /// </summary>
    private void PaintFateCopies(Graphics g, List<(FateLabels.Card Card, RectangleF Rect, bool Hovered)> placed, int screenHeight)
    {
        var names = this.fateHandNames;
        if (names.Count == 0) { return; }

        var size = Math.Clamp(26f * screenHeight / 1017f, 18f, 44f);
        var font = this.CopyFont(size);
        foreach (var (card, rect, _) in placed)
        {
            if (card.Group != 0 || !names.TryGetValue(card.Name, out var count)) { continue; }

            var frame = rect;
            frame.Inflate(8, 8);
            g.DrawRectangle(CopyFrame, frame.X, frame.Y, frame.Width, frame.Height);

            var text = $"COPY - {count} in hand";
            var width = g.MeasureString(text, font).Width + (size * 0.6f);
            // Resmin alt kenarinda: TAKE (0.4) ile kartin aciklama yazisi (~0.68) arasi; ustte kartin adini kapatiyordu.
            var tag = new RectangleF(rect.X + (rect.Width / 2f) - (width / 2f), rect.Y + (rect.Height * 0.53f), width, size);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Pill(tag)) { g.FillPath(CopyBack, path); }

            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(text, font, CopyText, tag, center);
            }

            g.SmoothingMode = SmoothingMode.None;
        }
    }

    private Font? copyFont;

    private Font CopyFont(float size)
    {
        if (this.copyFont is not { } font || Math.Abs(font.Size - (size * 0.55f)) > 0.1f)
        {
            font = this.copyFont = new Font("Segoe UI", size * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        return font;
    }

    private Font BadgeFont(float badgeSize)
    {
        if (this.planBadgeFont is not { } font || Math.Abs(font.Size - (badgeSize * 0.5f)) > 0.1f)
        {
            font = this.planBadgeFont = new Font("Segoe UI", badgeSize * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        return font;
    }

    private void PaintFateDraft(Graphics g, FateLabels.Card pick,
        List<(FateLabels.Card Card, RectangleF Rect, bool Hovered)> placed, int screenHeight)
    {
        var index = placed.FindIndex(p => p.Card.Body == pick.Body);
        if (index < 0) { return; }

        var rect = placed[index].Rect;
        var frame = rect;
        frame.Inflate(3, 3);
        g.DrawRectangle(PlanFrame, frame.X, frame.Y, frame.Width, frame.Height);

        var badgeSize = Math.Clamp(36f * screenHeight / 1017f, 22f, 60f);
        var badgeFont = this.BadgeFont(badgeSize);
        const string take = "TAKE";
        var textWidth = g.MeasureString(take, badgeFont).Width;
        var pill = new RectangleF(rect.X + (rect.Width / 2f) - ((textWidth + (badgeSize * 0.6f)) / 2f),
            rect.Y + (rect.Height * 0.4f) - (badgeSize / 2f), textWidth + (badgeSize * 0.6f), badgeSize);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Pill(pill))
        {
            g.FillPath(PlanBadgeBack, path);
            g.DrawPath(PlanBadgeEdge, path);
        }

        using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString(take, badgeFont, PlanBadgeText, pill, center);
        }

        g.SmoothingMode = SmoothingMode.None;
    }

    private static GraphicsPath Pill(RectangleF r)
    {
        var d = r.Height;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }
}
