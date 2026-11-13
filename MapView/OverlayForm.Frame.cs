using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Poe2Map;

/// <summary>
///     Kare dongusu.
///
///     Eskiden 16 ms'lik System.Windows.Forms.Timer vardi. WM_TIMER ~15.6 ms'lik sistem adimina yuvarlaniyor: 16 ms
///     istenince 31 ms geliyor. Olculen (19:06): 5 saniyede 167 kare = saniyede ~33 guncelleme, oyun ise 60+ kare cizerken.
///     Overlay her ikinci oyun karesinde geride kaliyordu; hareket ederken (kaydirma, zoom, yurume) harita ile cizgiler
///     arasinda git-gel olarak gorunuyor.
///
///     Simdi arka planda bir is parcacigi DWM'nin bir sonraki birlestirmesini bekliyor (DwmFlush: ekran yenileme hizi,
///     60 / 144 Hz) ve her seferinde arayuz is parcacigina TEK bir kare adimi gonderiyor. Onceki adim bitmediyse yenisi
///     eklenmiyor, yani arayuz takilirsa kuyruk birikmiyor. Eski zamanlayici yedek olarak kaliyor: is parcacigi olurse ya
///     da DwmFlush uzun bloklarsa harita 100 ms'de bir yine de guncellenir.
/// </summary>
internal sealed partial class OverlayForm
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private const int WatchdogMs = 100;
    private const long StaleMs = 150;
    private const long IdleStepMs = 100;
    private const double MinStepMs = 5.5;
    private long lastDispatch;

    private Thread? pacer;
    private volatile bool pacing;
    private volatile bool idle;
    private int stepPending;
    private long lastStep;

    /// <summary>Tutamac olusunca: BeginInvoke ancak o zaman calisiyor.</summary>
    private void StartPacer()
    {
        if (this.pacer is not null) { return; }

        this.pacing = true;
        this.pacer = new Thread(this.PaceLoop) { IsBackground = true, Name = "overlay-kare" };
        this.pacer.Start();
        this.Disposed += (_, _) => this.pacing = false;
    }

    private void PaceLoop()
    {
        var last = Stopwatch.GetTimestamp();
        while (this.pacing)
        {
            var result = DwmFlush();
            var now = Stopwatch.GetTimestamp();
            var waited = (now - last) * 1000.0 / Stopwatch.Frequency;
            last = now;

            // DWM kapali / ekran uykuda ise DwmFlush hata verip ya da hemen donup islemciyi yakmasin.
            if (result != 0 || waited < 2.0) { Thread.Sleep(4); }

            if (!this.IsHandleCreated || this.IsDisposed) { continue; }

            // Overlay gizliyken (oyun on planda degil) ekran hizinda okumaya gerek yok: 100 ms'de bir yeter.
            if (this.idle && Environment.TickCount64 - this.lastStep < IdleStepMs) { continue; }

            // Ust sinir: 240 Hz ekranda DwmFlush saniyede 240 kez doner (olculdu); tam ekran bir katmani o hizda yeniden
            // cizmek bosuna islemci ve DWM yuku. 5.5 ms alti araliklar atlaniyor: 240 Hz -> 120, 144/165 Hz -> oldugu gibi.
            if ((now - this.lastDispatch) * 1000.0 / Stopwatch.Frequency < MinStepMs) { continue; }

            // Onceki adim henuz islenmediyse (arayuz mesgul) yenisini ekleme.
            if (Interlocked.Exchange(ref this.stepPending, 1) != 0) { continue; }

            try
            {
                this.lastDispatch = now;
                this.BeginInvoke(new Action(this.FrameStep));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // Pencere kapaniyor ya da henuz yok; sonraki turda yeniden denenir.
                Interlocked.Exchange(ref this.stepPending, 0);
            }
        }
    }

    /// <summary>Yedek zamanlayici: kare dongusu 150 ms'den uzun sessiz kalmissa bir adim at.</summary>
    private void WatchdogTick()
    {
        if (Environment.TickCount64 - this.lastStep < StaleMs) { return; }
        if (Interlocked.Exchange(ref this.stepPending, 1) != 0) { return; }

        this.FrameStep();
    }

    /// <summary>Bir kare: oku, pencereyi oyuna oturt, gorunurlugu ayarla, hemen ciz.</summary>
    private void FrameStep()
    {
        try
        {
            this.lastStep = Environment.TickCount64;
            this.stats.Tick(this.Visible);
            this.ReadLive();
            this.TrackGameWindow();
            this.FollowGameFocus();

            // Okuma ile cizim ayni adimda: Invalidate sonra mesaj dongusune birakmak okuma ile cizim arasinda bir ya da
            // iki kare bekletiyordu.
            if (this.Visible)
            {
                this.Invalidate();
                this.Update();
            }

            // "Bazen titriyor" tanisi: sadece bir sey olduysa, bes saniyede bir satir; ayrica seyrek bir hiz ozeti.
            if (this.stats.Flush() is { } line) { CrashLog.Write(line, null); }
            if (this.stats.Summary() is { } summary) { CrashLog.Write(summary, null); }

            this.idle = !this.Visible;
        }
        finally
        {
            Interlocked.Exchange(ref this.stepPending, 0);
        }
    }
}
