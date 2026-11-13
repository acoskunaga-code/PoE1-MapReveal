using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Poe2Map;

internal sealed partial class IconsPanel
{
    /// <summary>
    ///     Kategori basligi: ok (kapaliyken saga, aciksa asagi), ad ve simge sayisi. Baslik cizgisinin tamami
    ///     tiklanabilir. Elle cizim: ok karakterleri yazi tipine bagli olmasin.
    /// </summary>
    private sealed class CategoryHeader : Control
    {
        private readonly string title;
        private readonly string count;
        private readonly Font bold = new(DefaultFont, FontStyle.Bold);
        private bool hover;

        internal CategoryHeader(string title, int count)
        {
            this.title = title;
            this.count = $"({count})";
            this.Cursor = Cursors.Hand;
            this.SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
                true);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool Open { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            this.hover = true;
            this.Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            this.hover = false;
            this.Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(this.hover ? SystemColors.ControlLight : SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var middle = this.Height / 2f;
            var arrow = this.Open
                ? new[] { new PointF(9, middle - 3), new PointF(19, middle - 3), new PointF(14, middle + 4) }
                : new[] { new PointF(11, middle - 5), new PointF(11, middle + 5), new PointF(18, middle) };
            g.FillPolygon(SystemBrushes.ControlDarkDark, arrow);

            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            var titleWidth = TextRenderer.MeasureText(g, this.title, this.bold, Size.Empty, flags).Width;
            TextRenderer.DrawText(
                g, this.title, this.bold, new Rectangle(28, 0, this.Width - 28, this.Height), SystemColors.ControlText, flags);
            TextRenderer.DrawText(
                g, this.count, this.Font, new Rectangle(28 + titleWidth + 6, 0, 60, this.Height), SystemColors.GrayText, flags);

            g.DrawLine(SystemPens.ControlDark, 0, this.Height - 1, this.Width, this.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { this.bold.Dispose(); }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///     Cap cetveli. Fare tekerlegi cetveli degil listeyi kaydiriyor: yuzlerce cetvelin arasinda gezerken
    ///     tekerlek yanlislikla degerleri bozmasin.
    /// </summary>
    private sealed class RulerBar : TrackBar
    {
        private readonly Action<int> scroll;

        internal RulerBar(Action<int> scroll)
        {
            this.scroll = scroll;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (e is HandledMouseEventArgs handled) { handled.Handled = true; }

            this.scroll(-Math.Sign(e.Delta) * RowHeight * 2);
        }
    }
}
