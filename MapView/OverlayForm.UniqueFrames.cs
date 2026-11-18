namespace Poe2Map;

/// <summary>
///     Uniques sekmesinde "Frame" isaretli unique yerdeyken oyunun kendi yer yazisi ("Heavy Belt" kutusu) altarlardaki
///     gibi kalin cerceveye aliniyor (kullanici 18.11.2026: "istedigimiz esyalar duserde altardaki gibi cerceve icine
///     alsana"). Yazi ogesi WorldItem varligindan GroundLabelMap ile (arayuz kok/1/6 +0x550 agaci) bulunuyor; arka planda
///     RefreshMonsters ile ayni turda, yalnizca isaretli bir unique yerdeyken. Cizim haritadan bagimsiz, her karede yazinin
///     o anki yerinde; yazi gizliyse (filtre gizlemis, ekran disi) cerceve yok. Renk unique adlarinin rengi.
/// </summary>
internal sealed partial class OverlayForm
{
    private HashSet<string> frameUniques = new(StringComparer.OrdinalIgnoreCase);
    private ulong[] uniqueFrameLabels = Array.Empty<ulong>();

    // Oyuncunun ekrandaki yeri (PaintOverlay, buyuk harita cizilirken). Harita kapaliysa kameranin odagi (lastCenter):
    // harita kaydirilmadikca oyuncu oradadir.
    private PointF playerScreen;
    private long playerScreenTick;

    /// <summary>Uniques sekmesinden: yer yazisi cercevelenecek unique'ler.</summary>
    internal void SetUniqueFrames(IEnumerable<string> names) =>
        this.frameUniques = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    /// <summary>Arka plan (RefreshMonsters, trackedUniques yazildiktan sonra): cercevelenecek yazi ogeleri.</summary>
    private void RefreshUniqueFrames(IntPtr h)
    {
        var frames = this.frameUniques;
        var wanted = frames.Count == 0
            ? new HashSet<ulong>()
            : this.trackedUniques.Where(u => UniqueNames(u.Name).Any(frames.Contains)).Select(u => u.Entity).ToHashSet();
        if (wanted.Count == 0 || this.MapElement == 0)
        {
            if (wanted.Count > 0 && this.lastFrameReport != "harita") { CrashLog.Write("unique cerceve: harita ogesi yok, yazilar aranmadi", null); this.lastFrameReport = "harita"; }
            this.uniqueFrameLabels = Array.Empty<ulong>();
            return;
        }

        var element = AltarLabels.Resolve(h, AltarLabels.RootOf(h, this.MapElement), GroundLabelMap.ElementPath);
        this.uniqueFrameLabels = element == 0 ? Array.Empty<ulong>() : GroundLabelMap.Read(h, element, wanted).Values.ToArray();

        // "Cerceve cikmiyor" sikayetinin cevabi gunlukte: kac unique istendi, kacinin yazisi bulundu (degisince).
        var report = $"{wanted.Count}/{this.uniqueFrameLabels.Length}/{element:X}";
        if (report != this.lastFrameReport)
        {
            this.lastFrameReport = report;
            CrashLog.Write($"unique cerceve: {wanted.Count} istenen yerde, {this.uniqueFrameLabels.Length} yazisi bulundu (yazi ogesi {element:X})", null);
        }
    }

    private string lastFrameReport = "";

    private void PaintUniqueFrames(PaintEventArgs e)
    {
        var labels = this.uniqueFrameLabels;
        var h = this.handle;
        if (labels.Length == 0 || h == IntPtr.Zero) { return; }

        using var pen = new Pen(this.groundUniqueText.Color, 3f);
        var width = this.ClientSize.Width;
        var height = this.ClientSize.Height;
        var from = this.PlayerOnScreen();
        foreach (var label in labels)
        {
            if (UiFinder.Reread(h, label) is not { Visible: true }) { continue; }
            if (PartsRect(h, new[] { label }, width, height) is not { } rect) { continue; }

            var frame = RectangleF.Inflate(rect, 3, 3);

            // Oyuncudan yazinin en yakin kenarina cizgi (kullanici 18.11.2026 cizerek istedi); cerceveden once, ustte kalsin.
            var to = new PointF(Math.Clamp(from.X, frame.Left, frame.Right), Math.Clamp(from.Y, frame.Top, frame.Bottom));
            if (!frame.Contains(from))
            {
                e.Graphics.DrawLine(AltarFrameEdge, from.X + 1, from.Y + 1, to.X + 1, to.Y + 1);
                e.Graphics.DrawLine(pen, from, to);
            }

            e.Graphics.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
            frame.Inflate(2.5f, 2.5f);
            e.Graphics.DrawRectangle(AltarFrameEdge, frame.X, frame.Y, frame.Width, frame.Height);
        }
    }

    /// <summary>Oyuncunun ekrandaki yeri: son yarim saniyede cizilen harita varsa oradan, yoksa kameranin odagi.</summary>
    private PointF PlayerOnScreen()
    {
        if (Environment.TickCount64 - this.playerScreenTick < 500) { return this.playerScreen; }

        return this.lastCenter is { X: > 0, Y: > 0 } center
            ? center
            : new PointF(this.ClientSize.Width / 2f, this.ClientSize.Height / 2f);
    }
}
