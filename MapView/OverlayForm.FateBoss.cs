using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     Ek kampanya bossu cikaran Fate kartlari one cikiyor: kalin pembe cerceve ve "+ CAMPAIGN BOSS" etiketi (kullanici:
///     ekrandaki yazilar Turkce olmasin).
///     Oyunun kart tablosu (AuguryCardTiers, 18.11.2026 bellekten): Savage, Pestilent ve Sadist kartlarinin uc tier'inda da
///     etki "Spawns an additional Boss from Campaign Areas without an Atlas equivalent" (oyun ici gruplari Savage, Filth,
///     Classic). Odul degil kart etkisi; haritalarda da calisiyor. Kullanici istegi 18.11.2026.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly string[] BossFates = { "Savage", "Pestilent", "Sadist" };
    private const string BossFateTag = "+ CAMPAIGN BOSS";
    private static readonly Pen FateBossFrame = new(Color.FromArgb(255, 60, 200), 4f) { LineJoin = LineJoin.Miter };
    private static readonly SolidBrush FateBossBack = new(Color.FromArgb(235, 70, 0, 50));
    private static readonly SolidBrush FateBossText = new(Color.FromArgb(255, 150, 230));

    private static bool IsBossFate(FateLabels.Card card) =>
        BossFates.Any(n => card.Name.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                           card.Name.StartsWith(n + " ", StringComparison.OrdinalIgnoreCase));

    /// <summary>Kartin kendisine cerceve, ortasina etiket (odul simgeleri altta, ad seridi ustte; orta resim bos).</summary>
    private static void PaintBossFates(Graphics g, IEnumerable<(FateLabels.Card Card, RectangleF Rect, bool Hovered)> placed, Font font)
    {
        foreach (var (card, rect, _) in placed)
        {
            if (!IsBossFate(card)) { continue; }

            var frame = RectangleF.Inflate(rect, 3, 3);
            g.DrawRectangle(AltarFrameEdge, frame.X - 3, frame.Y - 3, frame.Width + 6, frame.Height + 6);
            g.DrawRectangle(FateBossFrame, frame.X, frame.Y, frame.Width, frame.Height);

            var size = g.MeasureString(BossFateTag, font);
            var tag = new RectangleF(rect.X + ((rect.Width - size.Width) / 2f) - 4, rect.Y + (rect.Height * 0.42f), size.Width + 8, size.Height + 4);
            g.FillRectangle(FateBossBack, tag);
            g.DrawRectangle(FateBossFrame, tag.X, tag.Y, tag.Width, tag.Height);
            g.DrawString(BossFateTag, font, FateBossText, tag.X + 4, tag.Y + 2);
        }
    }
}
