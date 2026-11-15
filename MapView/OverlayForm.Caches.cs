using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     Djinn's Cache sandiklari (CacheReader): madenin renginde kucuk bir sandik ve yaninda ad + icerik ("Silver Djinn's
///     Cache: Weapons"). Varlik gezintisi canavarlarla ortak (RefreshMonsters), uyuyan liste dahil: sandik yerinden
///     oynamiyor. Konum her karede okunuyor. Acilan sandigi tanimak icin OverlayForm.CacheWatch.cs.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly SolidBrush BronzeBrush = new(Color.FromArgb(215, 135, 60));
    private static readonly SolidBrush SilverBrush = new(Color.FromArgb(210, 215, 230));
    private static readonly SolidBrush GoldBrush = new(Color.FromArgb(255, 205, 40));
    private static readonly SolidBrush CacheOtherBrush = new(Color.FromArgb(200, 120, 255));
    private static readonly Pen CacheLidPen = new(Color.FromArgb(40, 30, 20), 1.5f);

    private bool djinnCaches = true;
    private (ulong Render, CacheReader.Marker Marker)[] trackedCaches = Array.Empty<(ulong, CacheReader.Marker)>();
    private readonly byte[] cachePosition = new byte[8];

    /// <summary>Djinn's Cache isaretlerini ac / kapat (ayar penceresi, Altars sekmesi).</summary>
    internal void SetDjinnCaches(bool on)
    {
        this.djinnCaches = on;
        if (!on) { this.trackedCaches = Array.Empty<(ulong, CacheReader.Marker)>(); }
    }

    /// <summary>Varlik bir Djinn's Cache ise listeye ekler. RefreshMonsters'tan, onbellek kilidi altinda.</summary>
    private void CollectCache(
        IntPtr h, ulong entity, EntityReader.EntityInfo info, List<(ulong, CacheReader.Marker)> into, HashSet<ulong> done)
    {
        info.Cache ??= CacheReader.Classify(h, info);
        if (info.Cache is not { Metal: not CacheReader.Metal.None } marker) { return; }
        if (!info.Components.TryGetValue("Render", out var render) || !EntityReader.OwnedBy(h, render, entity)) { return; }
        if (!done.Add(render)) { return; }
        if (this.CacheOpened(h, entity, info, marker)) { return; }

        into.Add((render, marker));
    }

    /// <summary>Haritanin donusumuyle sandik isaretleri; canavarlarin ustune, simgelerle ayni katta.</summary>
    private void PaintCaches(PaintEventArgs e, Matrix transform)
    {
        var list = this.trackedCaches;
        var h = this.handle;
        if (list.Length == 0 || h == IntPtr.Zero) { return; }

        var points = new PointF[list.Length];
        var valid = new bool[list.Length];
        for (var i = 0; i < list.Length; i++)
        {
            if (!EntityReader.Read(h, list[i].Render + EntityReader.RenderPosition, this.cachePosition)) { continue; }

            var x = BitConverter.ToSingle(this.cachePosition, 0);
            var y = BitConverter.ToSingle(this.cachePosition, 4);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0) { continue; }

            points[i] = new PointF((x / PlayerPos.UnitsPerCell) + this.MapOffsetX, (y / PlayerPos.UnitsPerCell) + this.MapOffsetY);
            valid[i] = true;
        }

        transform.TransformPoints(points);

        var width = this.ClientSize.Width;
        var height = this.ClientSize.Height;
        for (var i = 0; i < list.Length; i++)
        {
            var p = points[i];
            if (!valid[i] || p.X < -20 || p.Y < -20 || p.X > width + 20 || p.Y > height + 20) { continue; }

            var marker = list[i].Marker;
            var brush = marker.Metal switch
            {
                CacheReader.Metal.Bronze => BronzeBrush,
                CacheReader.Metal.Silver => SilverBrush,
                CacheReader.Metal.Gold => GoldBrush,
                _ => CacheOtherBrush,
            };

            // Sandik: govde + kapak cizgisi, koyu kenar.
            var box = new RectangleF(p.X - 8, p.Y - 6, 16, 12);
            e.Graphics.FillRectangle(brush, box);
            e.Graphics.DrawRectangle(MonsterOutline, box.X, box.Y, box.Width, box.Height);
            e.Graphics.DrawLine(CacheLidPen, box.Left + 1, p.Y - 1, box.Right - 1, p.Y - 1);

            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            e.Graphics.DrawString(marker.Label, IconLabelFont, LabelShadow, p.X + 12, p.Y - 6);
            e.Graphics.DrawString(marker.Label, IconLabelFont, brush, p.X + 11, p.Y - 7);
        }
    }
}
