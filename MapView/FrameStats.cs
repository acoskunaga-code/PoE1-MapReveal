using System.Diagnostics;

namespace Poe2Map;

/// <summary>
///     Titreme / takilma tanisi. "Bazen titriyor" gibi aralikli bir sikayetin nedeni gozle bulunmaz; bu sinif 60 Hz
///     zamanlayicinin kare araliklarini, cizim surelerini, harita merkezinin kare kare salinimini ve pencere
///     degisikliklerini sayar ve YALNIZCA bir sey olduysa bes saniyede bir tek satir verir (gunluge yazilir).
///
///     Okuma kilavuzu:
///       takilma  : iki kare arasi 50 ms'yi asti (beklenen 16) - arayuz is parcacigi baska bir isle mesguldu
///       cizim    : bir karenin cizimi 25 ms'yi asti
///       salinim  : merkez 0.4 piksel ustunde ters yone gitti (kare kare git-gel); en buyuk genlik px
///       sicrama  : merkez bir karede 40 pikselden fazla atladi (kaydirma / zoom disinda beklenmez)
///       sinir    : overlay penceresinin konumu / boyutu degisti
///       harita   : buyuk harita (Tab) acik/kapali durumu degisti
///       gorunurluk: overlay gizlendi/gosterildi sayisi (hizli tekrar = odak cekismesi)
/// </summary>
internal sealed class FrameStats
{
    private const double GapMs = 50;
    private const double PaintMs = 25;
    private const double OscillationPx = 0.4;
    private const double JumpPx = 40;
    private const long FlushMs = 5000;

    private long lastTick;
    private long windowStart = Stopwatch.GetTimestamp();

    private int frames;
    private int gaps;
    private double worstGap;
    private int slowPaints;
    private double worstPaint;
    private int oscillations;
    private double worstOscillation;
    private int jumps;
    private int boundsChanges;
    private int mapFlips;
    private int visibilityFlips;
    private int topMostRestores;

    private PointF lastCenter;
    private PointF lastDelta;
    private bool haveCenter;

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    ///     Kare dongusu her adim attiginda. active: overlay su an gorunur mu; gizliyken adimlar bilerek seyrek (100 ms),
    ///     o araliklar takilma sayilmasin.
    /// </summary>
    internal void Tick(bool active)
    {
        var now = Stopwatch.GetTimestamp();
        this.frames++;
        if (active && this.lastTick != 0)
        {
            var gap = Ms(now - this.lastTick);
            if (gap > GapMs)
            {
                this.gaps++;
                this.worstGap = Math.Max(this.worstGap, gap);
            }
        }

        this.lastTick = active ? now : 0;
    }

    internal void Paint(long startTimestamp)
    {
        var ms = Ms(Stopwatch.GetTimestamp() - startTimestamp);
        this.paintCount++;
        this.paintTotal += ms;
        this.paintWorstSummary = Math.Max(this.paintWorstSummary, ms);
        if (ms <= PaintMs) { return; }

        this.slowPaints++;
        this.worstPaint = Math.Max(this.worstPaint, ms);
    }

    private int paintCount;
    private double paintTotal;
    private double paintWorstSummary;
    private int summaryFrames;
    private long summaryStart = Stopwatch.GetTimestamp();
    private bool firstSummaryDone;

    /// <summary>
    ///     Sorun olsun olmasin, ara sira (ilki 10 sn sonra, sonra 60 sn'de bir) kare hizi ve ortalama cizim suresi: "kare
    ///     dongusu gercekten ekran hizinda mi" sorusunun cevabi gunlukte dursun. Cizim yapilmadiysa (overlay gizli) null.
    /// </summary>
    internal string? Summary()
    {
        this.summaryFrames++;
        var elapsed = Ms(Stopwatch.GetTimestamp() - this.summaryStart);
        if (elapsed < (this.firstSummaryDone ? 60000 : 10000)) { return null; }

        string? line = null;
        if (this.paintCount > 0)
        {
            line = $"kare ozeti: {this.summaryFrames * 1000.0 / elapsed:F0} adim/sn, {this.paintCount * 1000.0 / elapsed:F0} cizim/sn, " +
                   $"cizim ortalama {this.paintTotal / this.paintCount:F1} ms (en uzun {this.paintWorstSummary:F1} ms)";
        }

        this.firstSummaryDone = true;
        this.summaryStart = Stopwatch.GetTimestamp();
        this.summaryFrames = this.paintCount = 0;
        this.paintTotal = this.paintWorstSummary = 0;
        return line;
    }

    /// <summary>Kararlastirilmis (olu bolgeden gecmis) degil, HAM merkez: salinim olu bolgenin yuttugu yer.</summary>
    internal void Center(PointF raw)
    {
        if (this.haveCenter)
        {
            var delta = new PointF(raw.X - this.lastCenter.X, raw.Y - this.lastCenter.Y);
            if (Math.Abs(delta.X) > JumpPx || Math.Abs(delta.Y) > JumpPx)
            {
                this.jumps++;
            }
            else
            {
                // Ayni eksende ard arda iki kucuk ama ters yonlu adim = git-gel.
                foreach (var (now, before) in new[] { (delta.X, this.lastDelta.X), (delta.Y, this.lastDelta.Y) })
                {
                    if (Math.Abs(now) > OscillationPx && Math.Abs(before) > OscillationPx && Math.Sign(now) != Math.Sign(before))
                    {
                        this.oscillations++;
                        this.worstOscillation = Math.Max(this.worstOscillation, Math.Min(Math.Abs(now), Math.Abs(before)));
                    }
                }
            }

            this.lastDelta = delta;
        }

        this.lastCenter = raw;
        this.haveCenter = true;
    }

    internal void BoundsChanged() => this.boundsChanges++;

    internal void MapFlipped() => this.mapFlips++;

    internal void VisibilityFlipped() => this.visibilityFlips++;

    internal void TopMostRestored() => this.topMostRestores++;

    /// <summary>Bes saniye doldu ve bir sey olduysa tek satir, yoksa null. Sayaclar her seferinde sifirlanir.</summary>
    internal string? Flush()
    {
        var now = Stopwatch.GetTimestamp();
        if (Ms(now - this.windowStart) < FlushMs) { return null; }

        string? line = null;
        if (this.gaps + this.slowPaints + this.oscillations + this.jumps + this.boundsChanges + this.mapFlips +
            this.visibilityFlips + this.topMostRestores > 0)
        {
            line = $"kare tanisi ({this.frames} kare): takilma {this.gaps} (en uzun {this.worstGap:F0} ms)  " +
                   $"cizim {this.slowPaints} (en uzun {this.worstPaint:F0} ms)  " +
                   $"salinim {this.oscillations} (genlik {this.worstOscillation:F1} px)  sicrama {this.jumps}  " +
                   $"sinir {this.boundsChanges}  harita {this.mapFlips}  gorunurluk {this.visibilityFlips}  " +
                   $"ust-duzeltme {this.topMostRestores}";
        }

        this.windowStart = now;
        this.frames = this.gaps = this.slowPaints = this.oscillations = this.jumps = this.boundsChanges = this.mapFlips =
            this.visibilityFlips = this.topMostRestores = 0;
        this.worstGap = this.worstPaint = this.worstOscillation = 0;
        return line;
    }
}
