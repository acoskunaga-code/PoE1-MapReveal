using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     The Viridian Wildwood: wisp'ler renkli noktalar (Primal mavi, Vivid sari, Wild mor; buyuk wisp daha buyuk
///     nokta), yakit kuresi halka + ad, Funerary altarlari elmas + ad, tanimadigimiz Azmeri nesneleri kare + ad.
///     Okuma WildwoodReader'da; varlik gezintisi canavarlarla ortak (RefreshMonsters), uyuyan liste dahil:
///     wisp'ler yerinden oynamiyor, uzaktakiler de dogru yerde. Konum her karede okunuyor.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly SolidBrush PrimalBrush = new(Color.FromArgb(70, 150, 255));
    private static readonly SolidBrush VividBrush = new(Color.FromArgb(255, 220, 50));
    private static readonly SolidBrush WildBrush = new(Color.FromArgb(190, 90, 255));
    private static readonly Pen FuelPen = new(Color.FromArgb(150, 255, 255), 3f);
    private static readonly SolidBrush AltarBrush = new(Color.FromArgb(255, 140, 40));
    private static readonly SolidBrush NamelessShrineBrush = new(Color.FromArgb(80, 230, 160));
    private static readonly SolidBrush OtherBrush = new(Color.FromArgb(230, 230, 230));
    private static readonly SolidBrush WildwoodLabelBrush = new(Color.FromArgb(230, 240, 255));

    private bool wildwood = true;
    private (ulong Render, WildwoodReader.Marker Marker)[] trackedWildwood = Array.Empty<(ulong, WildwoodReader.Marker)>();
    private readonly byte[] wildwoodPosition = new byte[8];
    private readonly HashSet<string> loggedWildwood = new(StringComparer.Ordinal);

    /// <summary>Wildwood isaretlerini ac / kapat (ayar penceresi).</summary>
    internal void SetWildwood(bool on)
    {
        this.wildwood = on;
        if (!on) { this.trackedWildwood = Array.Empty<(ulong, WildwoodReader.Marker)>(); }
    }

    /// <summary>Varlik bir Wildwood nesnesiyse listeye ekler. RefreshMonsters'tan, onbellek kilidi altinda.</summary>
    private void CollectWildwood(
        IntPtr h, ulong entity, EntityReader.EntityInfo info, List<(ulong, WildwoodReader.Marker)> into, HashSet<ulong> done)
    {
        // Classify yalnizca animasyon varligi henuz yuklenmemis wisp icin null donuyor. Vazgecmiyoruz: ilk 8 tur her
        // turda, sonra 4 turda bir (saniyede bir). Eskiden 5 denemeden (1,25 sn) sonra o wisp bir daha hic cizilmiyordu.
        if (info.Wildwood is null)
        {
            info.WildwoodTries++;
            if (info.WildwoodTries <= 8 || info.WildwoodTries % 4 == 0) { info.Wildwood = WildwoodReader.Classify(h, info); }
        }

        if (info.Wildwood is not { Kind: not WildwoodReader.Kind.None } marker) { return; }
        if (!info.Components.TryGetValue("Render", out var render) || !EntityReader.OwnedBy(h, render, entity)) { return; }
        if (!done.Add(render)) { return; }

        into.Add((render, marker));

        // Tanimadigimiz Azmeri nesneleri (altar, isik kuresi) gunlukte: yollari bir sonraki adim icin lazim.
        if (marker.Kind is WildwoodReader.Kind.Other or WildwoodReader.Kind.Altar && this.loggedWildwood.Add(info.Path))
        {
            CrashLog.Write($"wildwood nesnesi: {marker.Kind} \"{marker.Label}\" {info.Path}", null);
        }
    }

    /// <summary>Haritanin donusumuyle Wildwood isaretleri; canavarlarin altina.</summary>
    private void PaintWildwood(PaintEventArgs e, Matrix transform)
    {
        var list = this.trackedWildwood;
        var h = this.handle;
        if (list.Length == 0 || h == IntPtr.Zero) { return; }

        var points = new PointF[list.Length];
        var valid = new bool[list.Length];
        for (var i = 0; i < list.Length; i++)
        {
            if (!EntityReader.Read(h, list[i].Render + EntityReader.RenderPosition, this.wildwoodPosition)) { continue; }

            var x = BitConverter.ToSingle(this.wildwoodPosition, 0);
            var y = BitConverter.ToSingle(this.wildwoodPosition, 4);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0) { continue; }

            points[i] = new PointF((x / PlayerPos.UnitsPerCell) + this.MapOffsetX, (y / PlayerPos.UnitsPerCell) + this.MapOffsetY);
            valid[i] = true;
        }

        transform.TransformPoints(points);

        var saved = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var width = this.ClientSize.Width;
        var height = this.ClientSize.Height;

        for (var i = 0; i < list.Length; i++)
        {
            var p = points[i];
            if (!valid[i] || p.X < -20 || p.Y < -20 || p.X > width + 20 || p.Y > height + 20) { continue; }

            var marker = list[i].Marker;
            switch (marker.Kind)
            {
                case WildwoodReader.Kind.Primal:
                case WildwoodReader.Kind.Vivid:
                case WildwoodReader.Kind.Wild:
                    var d = marker.Size switch { 3 => 13f, 2 => 9f, _ => 6f };
                    var brush = marker.Kind switch
                    {
                        WildwoodReader.Kind.Primal => PrimalBrush,
                        WildwoodReader.Kind.Vivid => VividBrush,
                        _ => WildBrush,
                    };
                    e.Graphics.FillEllipse(brush, p.X - (d / 2f), p.Y - (d / 2f), d, d);
                    e.Graphics.DrawEllipse(MonsterOutline, p.X - (d / 2f), p.Y - (d / 2f), d, d);
                    break;

                case WildwoodReader.Kind.Fuel:
                    e.Graphics.DrawEllipse(FuelPen, p.X - 9, p.Y - 9, 18, 18);
                    this.WildwoodLabel(e, marker.Label, p, 12);
                    break;

                case WildwoodReader.Kind.NamelessShrine:
                    // Ucgen: kare (taninmayan) ve elmastan (altar) ayrilsin.
                    e.Graphics.FillPolygon(NamelessShrineBrush, new[] { new PointF(p.X, p.Y - 10), new PointF(p.X + 9, p.Y + 7), new PointF(p.X - 9, p.Y + 7) });
                    e.Graphics.DrawPolygon(MonsterOutline, new[] { new PointF(p.X, p.Y - 10), new PointF(p.X + 9, p.Y + 7), new PointF(p.X - 9, p.Y + 7) });
                    this.WildwoodLabel(e, marker.Label, p, 12);
                    break;

                case WildwoodReader.Kind.Altar:
                    e.Graphics.FillPolygon(AltarBrush, new[] { new PointF(p.X, p.Y - 9), new PointF(p.X + 9, p.Y), new PointF(p.X, p.Y + 9), new PointF(p.X - 9, p.Y) });
                    this.WildwoodLabel(e, marker.Label, p, 12);
                    break;

                default:
                    e.Graphics.FillRectangle(OtherBrush, p.X - 5, p.Y - 5, 10, 10);
                    this.WildwoodLabel(e, marker.Label, p, 8);
                    break;
            }
        }

        e.Graphics.SmoothingMode = saved;
    }

    private void WildwoodLabel(PaintEventArgs e, string text, PointF at, float offset)
    {
        if (text.Length == 0) { return; }

        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        e.Graphics.DrawString(text, IconLabelFont, LabelShadow, at.X + offset + 1, at.Y - 6);
        e.Graphics.DrawString(text, IconLabelFont, WildwoodLabelBrush, at.X + offset, at.Y - 7);
    }
}
