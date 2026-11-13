namespace Poe2Map;

/// <summary>
///     Yakalanmayan hatalari ve acilis/kapanis anlarini diske yazar.
///     Uygulama sessizce kapandiginda sebebi buradan okunuyor.
///
///     Iki exe de kullaniyor; dosya adi kayitlar birbirine karismasin diye exe'ye gore ayriliyor: mapview.log, mapoverlay.log.
///     Ad exe dosyasinin adindan DEGIL <see cref="Channel"/>'dan geliyor: exe'nin adi kullanicinin degistirebilecegi bir sey
///     (dosyayi elle yeniden adlandirmak ya da AssemblyName) ve gunluk bundan dolayi ikiye bolunmesin. Channel bos ise eski
///     davranis: calisan exe'nin adi.
/// </summary>
internal static class CrashLog
{
    /// <summary>
    ///     Gunluk bu boyutu gecince .old.log'a tasinir (oncekinin ustune) ve yenisi baslar: diskte en fazla ~2 MB.
    ///     13.11.2026'ya kadar sinir yoktu, gunluk her calismada buyuyordu.
    /// </summary>
    private const long MaxBytes = 1024 * 1024;

    // Birden fazla is parcacigi yaziyor (cizim, arka plan okumalari); ayni anda iki ekleme ya da tasima olmasin.
    private static readonly object Sync = new();

    internal static string? Channel { get; set; }

    internal static string Path_ => AppPaths.File(
        (Channel ?? System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "poe1-map").ToLowerInvariant() + ".log");

    internal static void Write(string kind, Exception? ex)
    {
        try
        {
            var line = $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}  {kind}" +
                       (ex is null ? "" : Environment.NewLine + ex) + Environment.NewLine;
            lock (Sync)
            {
                var path = Path_;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(path, System.IO.Path.ChangeExtension(path, ".old.log"), overwrite: true);
                }

                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Gunluk yazilamazsa uygulamayi durdurmayalim.
        }
    }
}
