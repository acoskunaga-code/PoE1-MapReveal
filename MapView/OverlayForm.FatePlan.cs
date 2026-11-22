using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     "Invoke Fates" paneli acikken en iyi secim (FatePlanner): eldeki onerilen kartlara tiklama sirasi numarasi ve yesil
///     cerceve, eldeki kartlarin altina (sigmazsa yanina) "bu sirayla tikla -> kazanc" kutusu. Odul degerleri ayar
///     penceresinin Fates sekmesinden degisiyor (FateValues).
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly SolidBrush PlanBadgeBack = new(Color.FromArgb(240, 30, 150, 70));
    private static readonly SolidBrush PlanBadgeText = new(Color.White);
    private static readonly Pen PlanBadgeEdge = new(Color.FromArgb(255, 10, 40, 20), 2f);
    private static readonly Pen PlanFrame = new(Color.FromArgb(60, 220, 110), 3f);
    private static readonly SolidBrush PlanTitle = new(Color.FromArgb(120, 235, 140));
    private static readonly SolidBrush PlanBadgeDone = new(Color.FromArgb(200, 90, 90, 90));

    private bool fatePlanEnabled = true;
    private FateValues fateValues = FateValues.Default;
    private string fateAdviceLogged = "";
    private FatePlanner.Plan? fatePlan;
    private HashSet<ulong> fatePlanTaken = new();
    private Font? planBadgeFont;

    // Select a Fate onerisi ve Invoke Fates'teki yuva sayisi (son gorulen; gorulmeden once 3).
    private (FateLabels.Card Pick, FatePlanner.Plan Plan)? fateDraft;
    private int invokeSlots = 3;

    /// <summary>Oneri acik mi ve odul degerleri ("yazi = chaos" satirlari).</summary>
    internal void SetFatePlan(bool enabled, IEnumerable<string> values)
    {
        this.fatePlanEnabled = enabled;
        this.fateValues = new FateValues(values);
        if (!enabled)
        {
            this.fatePlan = null;
            this.fateDraft = null;
        }

        if (!enabled && !this.fateEnabled) { this.fateCards = Array.Empty<FateLabels.Card>(); }
    }

    /// <summary>
    ///     Arka plan, RefreshFates'ten. Invoke Fates: eldeki kartlardan en iyi secim. Select a Fate: teklif edilen kartlardan
    ///     (grup 0) hangisi alinmali, eldekilerle (grup 1, ilk secimden sonra panelde gorunuyor) birlikte; bkz.
    ///     <see cref="FatePlanner.Draft"/>.
    /// </summary>
    private void UpdateFatePlan(IntPtr h, IReadOnlyList<FateLabels.Card> cards)
    {
        var hand = cards.Where(c => c.Group == 1).ToList();
        var invoke = this.fatePlanEnabled && FateLabels.IsInvoke(h, this.fatePanel);
        this.fateDraft = null;
        if (this.fatePlanEnabled && !invoke) { this.fateDraft = this.Draft(cards, hand); }
        else { this.fateHandNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); }
        if (!invoke || hand.Count == 0)
        {
            this.fatePlan = null;
            if (this.fateDraft is { } draft) { this.LogFateAdvice($"AL {draft.Pick.Name}", draft.Plan, cards); }

            return;
        }

        var picks = FateLabels.TopSlots(h, this.fatePanel);
        if (picks > 0) { this.invokeSlots = picks; }

        this.fatePlan = FatePlanner.Best(hand, picks > 0 ? picks : this.invokeSlots, this.fateValues);
        if (this.fatePlan is { } advice) { this.LogFateAdvice("CAGIR", advice, hand); }

        // Uste konmus kartlar: eldeki ayni ad + ayni odullu kart (el sirasi uste konanlari da tutuyor).
        var taken = new HashSet<ulong>();
        foreach (var top in cards.Where(c => c.Group == 0))
        {
            var twin = hand.FirstOrDefault(c => !taken.Contains(c.Body) && c.Name == top.Name && c.Rewards.SequenceEqual(top.Rewards));
            if (twin is not null) { taken.Add(twin.Body); }
        }

        this.fatePlanTaken = taken;
    }

    /// <summary>"Oneri kotu" denince bakilacak yer: oneri degisince gorulen kartlar, oduller ve onerilen sira gunluge.</summary>
    private void LogFateAdvice(string what, FatePlanner.Plan plan, IEnumerable<FateLabels.Card> cards)
    {
        static string Reward(string reward)
        {
            var (text, need) = FatePlanner.Parse(reward);
            return need > 1 ? $"{FatePlanner.Short(text)} /{need}" : FatePlanner.Short(text);
        }

        var seen = string.Join("; ", cards.Select(c => $"{(c.Group == 0 ? "teklif" : "el")} {c.Name} [{string.Join(" + ", c.Rewards.Select(Reward))}]"));
        var order = string.Join(", ", plan.Order.Select(c => c.Name));
        var line = $"fate onerisi: {what}  sira {order}  -> {FatePlanner.Describe(plan)}  deger {plan.Score:F0}c  |  {seen}";
        if (line == this.fateAdviceLogged) { return; }

        this.fateAdviceLogged = line;
        CrashLog.Write(line, null);
    }

    /// <summary>PaintFates'in sonunda: kartlarin ve kutularin yeri belli olduktan sonra.</summary>
    private void PaintFatePlan(Graphics g, List<(FateLabels.Card Card, RectangleF Rect, bool Hovered)> placed, List<RectangleF> boxes,
        Font font, int screenHeight)
    {
        this.PaintFateCopies(g, placed, screenHeight);
        if (this.fateDraft is { } draft)
        {
            this.PaintFateDraft(g, draft.Pick, draft.Plan, placed, boxes, font, screenHeight);
            return;
        }

        if (this.fatePlan is not { Order.Count: > 0 } plan) { return; }

        var rects = new List<RectangleF>();
        var badgeSize = Math.Clamp(36f * screenHeight / 1017f, 22f, 60f);
        var badgeFont = this.BadgeFont(badgeSize);

        for (var i = 0; i < plan.Order.Count; i++)
        {
            var body = plan.Order[i].Body;
            var index = placed.FindIndex(p => p.Card.Body == body);
            if (index < 0) { continue; }

            var rect = placed[index].Rect;
            var done = this.fatePlanTaken.Contains(body);
            rects.Add(rect);
            var frame = rect;
            frame.Inflate(3, 3);
            if (!done) { g.DrawRectangle(PlanFrame, frame.X, frame.Y, frame.Width, frame.Height); }

            // Numara kartin resminin ortasinda (kutular kartlarin ustune binmiyor, resim hep acik). Uste konmus kartin
            // numarasi gri: kart elden kalkiyor ama numarasi oldugu yerde kaliyor, sira takip edilebilsin.
            var badge = new RectangleF(rect.X + (rect.Width / 2f) - (badgeSize / 2f), rect.Y + (rect.Height * 0.4f) - (badgeSize / 2f),
                badgeSize, badgeSize);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillEllipse(done ? PlanBadgeDone : PlanBadgeBack, badge);
            g.DrawEllipse(PlanBadgeEdge, badge);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString((i + 1).ToString(), badgeFont, PlanBadgeText, badge, center);
            g.SmoothingMode = SmoothingMode.None;
        }

        if (rects.Count == 0) { return; }

        var title = "Best: " + string.Join("  ", plan.Order.Select((c, i) => $"{i + 1} {c.Name}"));
        var gains = "-> " + FatePlanner.Describe(plan);
        var width = Math.Max(g.MeasureString(title, font).Width, g.MeasureString(gains, font).Width) + 14;
        width = Math.Min(width, this.ClientSize.Width - 8);
        var lineHeight = g.MeasureString(gains, font, (int)(width - 10)).Height;
        var height = font.GetHeight(g) + lineHeight + 14;

        var box = this.PlacePlanBox(rects, placed.Select(p => p.Rect).ToList(), boxes, width, height, screenHeight);
        g.FillRectangle(FateBack, box);
        g.DrawRectangle(PlanFrame, box.X, box.Y, box.Width, box.Height);
        g.DrawString(title, font, PlanTitle, box.X + 5, box.Y + 5);
        g.DrawString(gains, font, FateText, new RectangleF(box.X + 5, box.Y + 7 + font.GetHeight(g), box.Width - 10, lineHeight + 2));
    }

    /// <summary>Onerilen kartlarin altina ortali; sigmazsa ya da bir kutuya binerse solu, sagi; hicbiri olmazsa alt, ekrana sigdirilarak.</summary>
    private RectangleF PlacePlanBox(List<RectangleF> picked, List<RectangleF> cards, List<RectangleF> boxes, float width, float height, int screenHeight)
    {
        var row = picked.Aggregate(RectangleF.Union);
        var screen = new RectangleF(0, 0, this.ClientSize.Width, screenHeight);
        var options = new[]
        {
            new RectangleF(row.X + (row.Width / 2f) - (width / 2f), row.Bottom + 8, width, height),
            new RectangleF(row.X - width - 8, row.Y + (row.Height / 2f) - (height / 2f), width, height),
            new RectangleF(row.Right + 8, row.Y + (row.Height / 2f) - (height / 2f), width, height),
        };

        foreach (var option in options)
        {
            if (screen.Contains(option) && !cards.Any(c => c.IntersectsWith(option)) && !boxes.Any(b => b.IntersectsWith(option)))
            {
                return option;
            }
        }

        // boxes Confirm butonunu da tutuyor (PaintFates); bos yer yoksa butona binmeyen ilk yer.
        foreach (var option in options)
        {
            if (screen.Contains(option) && !this.fateButtonRects.Any(b => b.IntersectsWith(option))) { return option; }
        }

        var below = options[0];
        below.X = Math.Clamp(below.X, 0, Math.Max(0, screen.Width - width));
        below.Y = Math.Min(below.Y, screen.Height - height);
        return below;
    }
}
