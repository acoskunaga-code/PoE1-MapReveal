namespace Poe2Map;

/// <summary>
///     Ayarsiz haritanin giris noktasi. Pencere yok; butun is <see cref="OverlayContext"/>
///     icinde donuyor (tepsi simgesi + overlay). Uygulama kendi adiyla, oldugu yerden
///     calisir - kopyalanma, yeniden adlandirma, gizlenme yok.
///
///     Tek ornek: ayni anda iki kopya calisirsa iki overlay ust uste biner. Adlandirilmis
///     mutex ikinci calismayi sessizce durduruyor.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Gunluk adi exe adindan bagimsiz: exe yeniden adlandirilsa da kayitlar mapoverlay.log'da kalir.
        CrashLog.Channel = "mapoverlay";

        // Ayni surec icin tek ornek; mutex adi surece ozel.
        using var single = new Mutex(true, @"Local\Poe1Map.MapOverlay", out var owned);
        if (!owned) { return; }

        // Yakalanmayan her hata diske yazilsin; pencere olmadigi icin sessiz cokme
        // baska turlu gorulemez. Arayuz is parcacigindaki tek bir gecici okuma hatasi
        // da uygulamayi oldurmesin - harita kaybolmasin. Gunluk: <exe klasoru>\data\mapoverlay.log (AppPaths).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLog.Write("arayuz", e.Exception);
        AppDomain.CurrentDomain.UnhandledException +=
            (_, e) => CrashLog.Write("surec", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("gorev", e.Exception);
            e.SetObserved();
        };

        // Eski surumlerin %LOCALAPPDATA%\poe1-map klasoru (ayarlar dahil) exe'nin yanindaki data\'ya; tek ornek kesinlesince.
        var moved = AppPaths.MoveOldFiles();
        CrashLog.Write("basladi, dosyalar: " + AppPaths.Dir, null);
        if (moved is not null) { CrashLog.Write(moved, null); }
        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayContext());
        CrashLog.Write("normal kapandi", null);
    }
}
