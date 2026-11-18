namespace Poe2Map;

/// <summary>
///     Uniques sekmesinde "Alert" isaretli unique yerdeyken ekranin ust ortasinda buyuk harflerle adi, koyu bir kutuda;
///     buyuk harita kapaliyken de (OnPaint, haritadan bagimsiz). Birden fazlaysa alt alta, ayni addan birden fazlaysa
///     "x2". Alininca (varlik listeden cikinca) kayboluyor. Boyut ve renk ayar penceresinden, hepsi icin tek; varsayilan
///     hicbiri isaretli degil. Kullanici istegi 18.11.2026: "belirttigimiz esya ekranda buyuk puntolarla ismini
///     yazdirabilirmiyiz. buda kullanici insiyatifinde olsun".
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly SolidBrush UniqueAlertBack = new(Color.FromArgb(230, 20, 12, 6));

    private HashSet<string> alertUniques = new(StringComparer.OrdinalIgnoreCase);
    private Font uniqueAlertFont = new("Segoe UI", 36f, FontStyle.Bold);

    /// <summary>Uniques sekmesinden: buyuk adi yazilacak unique'ler ve punto boyu.</summary>
    internal void SetUniqueAlerts(IEnumerable<string> names, int size)
    {
        this.alertUniques = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        if (Math.Abs(this.uniqueAlertFont.Size - size) > 0.1f) { this.uniqueAlertFont = new Font("Segoe UI", size, FontStyle.Bold); }
    }

    private void PaintUniqueAlerts(PaintEventArgs e)
    {
        var alerts = this.alertUniques;
        var list = this.trackedUniques;
        if (alerts.Count == 0 || list.Length == 0 || this.handle == IntPtr.Zero) { return; }

        var lines = list.Select(u => u.Name)
            .Where(name => UniqueNames(name).Any(alerts.Contains))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() > 1 ? $"{g.Key.ToUpperInvariant()}  x{g.Count()}" : g.Key.ToUpperInvariant())
            .ToList();
        if (lines.Count == 0) { return; }

        var font = this.uniqueAlertFont;
        var text = this.groundUniqueText;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Ust orta, ekran yuksekliginin %12'si: oyunun sag ustteki gorev listesine ve ortadaki karaktere binmesin.
        var y = this.ClientSize.Height * 0.12f;
        foreach (var line in lines)
        {
            var size = e.Graphics.MeasureString(line, font);
            var box = new RectangleF((this.ClientSize.Width - size.Width) / 2f - 14, y, size.Width + 28, size.Height + 8);
            e.Graphics.FillRectangle(UniqueAlertBack, box);
            e.Graphics.DrawRectangle(MonsterOutline, box.X, box.Y, box.Width, box.Height);
            e.Graphics.DrawString(line, font, LabelShadow, box.X + 16, box.Y + 6);
            e.Graphics.DrawString(line, font, text, box.X + 14, box.Y + 4);
            y += box.Height + 6;
        }
    }
}
