using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     3.30.0c yamasinin (19.11.2026) yeni degerli Fate odulleri kartin ustunde buyuk etiketle: Divine Orb, Mirror Shard,
///     Fracturing Orb, Hinekora's Lock (kullanici 19.11.2026: "divine mirror ve fracturing orb ve hinekora cikarsa kartin
///     uzerinde yazdirabilirmisin"). Kartin gorunen odul yazilarinda (FateLabels.Card.Rewards) ad geciyorsa kalin altin
///     cerceve ve "+ DIVINE ORB" gibi etiket; odul kopya istiyorsa "(2 COPIES)". Ekrandaki yazi Ingilizce.
///     Kesin odul metinleri yamada henuz bir kartta gorulmedi: tekil/cogul ve kesme isareti farki tutsun diye kisa
///     parcayla araniyor.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly (string Needle, string Tag)[] ChaseRewards =
    {
        ("Divine Orb", "DIVINE ORB"),
        ("Mirror Shard", "MIRROR SHARD"),
        ("Fracturing Orb", "FRACTURING ORB"),
        ("Hinekora", "HINEKORA'S LOCK"),
    };

    private static readonly Pen FateChaseFrame = new(Color.FromArgb(255, 205, 60), 4f) { LineJoin = LineJoin.Miter };
    private static readonly SolidBrush FateChaseBack = new(Color.FromArgb(235, 45, 30, 0));
    private static readonly SolidBrush FateChaseText = new(Color.FromArgb(255, 225, 120));

    /// <summary>Kartin odullerinde gecen degerli oduller, etiket yazisiyla.</summary>
    private static List<string> ChaseTags(FateLabels.Card card)
    {
        var tags = new List<string>();
        foreach (var (needle, tag) in ChaseRewards)
        {
            var reward = card.Rewards.FirstOrDefault(r => r.Contains(needle, StringComparison.OrdinalIgnoreCase));
            if (reward is null) { continue; }

            var copies = reward.Contains("three copies", StringComparison.OrdinalIgnoreCase) ? " (3 COPIES)"
                : reward.Contains("two copies", StringComparison.OrdinalIgnoreCase) ? " (2 COPIES)"
                : "";
            tags.Add($"+ {tag}{copies}");
        }

        return tags;
    }

    /// <summary>Altin cerceve kartin kendisine; etiketler kartin ortasinin altinda (kampanya bossu etiketi %42'de).</summary>
    private static void PaintChaseFates(Graphics g, IEnumerable<(FateLabels.Card Card, RectangleF Rect, bool Hovered)> placed, Font font)
    {
        using var bold = new Font(font, FontStyle.Bold);
        foreach (var (card, rect, _) in placed)
        {
            var tags = ChaseTags(card);
            if (tags.Count == 0) { continue; }

            var frame = RectangleF.Inflate(rect, 8, 8);
            g.DrawRectangle(AltarFrameEdge, frame.X - 3, frame.Y - 3, frame.Width + 6, frame.Height + 6);
            g.DrawRectangle(FateChaseFrame, frame.X, frame.Y, frame.Width, frame.Height);

            var top = rect.Y + (rect.Height * 0.56f);
            foreach (var text in tags)
            {
                var size = g.MeasureString(text, bold);
                var tag = new RectangleF(rect.X + ((rect.Width - size.Width) / 2f) - 4, top, size.Width + 8, size.Height + 4);
                g.FillRectangle(FateChaseBack, tag);
                g.DrawRectangle(FateChaseFrame, tag.X, tag.Y, tag.Width, tag.Height);
                g.DrawString(text, bold, FateChaseText, tag.X + 4, tag.Y + 2);
                top = tag.Bottom + 4;
            }
        }
    }
}
