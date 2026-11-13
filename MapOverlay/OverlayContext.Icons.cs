namespace Poe2Map;

/// <summary>
///     Simge verisi yoksa (dagitilan surum): resim ilk acilista arka planda indirilir (IconDownload), adlar oyun bir alana
///     girdiginde oyunun kendi tablosundan okunur (IconTable). Her biri hazir olunca katalog yeniden yuklenir.
/// </summary>
internal sealed partial class OverlayContext
{
    private DateTime nextIconTable = DateTime.MinValue;
    private int iconTableBusy;
    private bool iconNamesReady;
    private bool iconTableFailLogged;

    private void FetchIconsIfMissing()
    {
        if (this.iconCatalog is { HasImages: true }) { return; }

        _ = Task.Run(async () =>
        {
            if (!await IconDownload.EnsureAsync().ConfigureAwait(false)) { return; }
            if (this.overlay.IsDisposed || !this.overlay.IsHandleCreated) { return; }

            this.overlay.BeginInvoke(this.UseDownloadedIcons);
        });
    }

    /// <summary>
    ///     Alan yuklenince ve sonra her tikte: catalog.txt yoksa simge adlarini oyunun tablosundan okuyup yazar (arka
    ///     planda). Basarisizsa 30 sn sonra ayni alanda da yeniden: 14.11.2026 test, alan yuklendigi anda okuma tutmadi
    ///     (3 simgeli bir alan), ayni alanda birkac dakika sonra `MapScan icontable` 708 satiri okudu.
    /// </summary>
    private void ReadIconNamesIfMissing(ulong area)
    {
        if (this.iconNamesReady || area == 0 || this.handle == IntPtr.Zero || DateTime.UtcNow < this.nextIconTable) { return; }
        if (File.Exists(IconDownload.CatalogFile))
        {
            this.iconNamesReady = true;
            return;
        }
        if (Interlocked.Exchange(ref this.iconTableBusy, 1) == 1) { return; }

        this.nextIconTable = DateTime.UtcNow.AddSeconds(30);
        var h = this.handle;
        _ = Task.Run(() =>
        {
            try
            {
                var started = Environment.TickCount64;
                if (IconTable.Read(h, area) is not { } rows)
                {
                    // Basarisizlik 30 sn'de bir tekrar edebilir; gunluge sadece ilki.
                    if (!this.iconTableFailLogged)
                    {
                        this.iconTableFailLogged = true;
                        CrashLog.Write("simge adlari oyundan okunamadi (alanda simge yok ya da tablo sinavi tutmadi), 30 sn'de bir yeniden denenecek", null);
                    }

                    return;
                }

                IconCatalogFile.Write(rows, IconDownload.CatalogFile, "oyunun MinimapIcons.dat tablosu (indeks = satir + 1)");
                CrashLog.Write($"simge adlari oyundan okundu: {rows.Count} simge, {Environment.TickCount64 - started} ms", null);
                if (!this.overlay.IsDisposed && this.overlay.IsHandleCreated) { this.overlay.BeginInvoke(this.UseDownloadedIcons); }
            }
            catch (Exception ex)
            {
                CrashLog.Write("simge adlari okunurken", ex);
            }
            finally
            {
                Interlocked.Exchange(ref this.iconTableBusy, 0);
            }
        });
    }

    /// <summary>Arayuz is parcaciginda: yeni katalogu yukler, haritaya ve (sonra acilacak) ayar penceresine verir.</summary>
    private void UseDownloadedIcons()
    {
        if (IconCatalog.Load() is not { } fresh) { return; }

        var old = this.iconCatalog;
        this.iconCatalog = fresh;
        this.ApplySettings();
        old?.Dispose();

        // Acik ayar penceresi eski (bos) katalogu tutuyor; kapatilip acilinca yenisini gosterir.
        CrashLog.Write($"simge katalogu yuklendi: {fresh.Categories.Sum(c => c.Entries.Count)} simge, resimler {(fresh.HasImages ? "var" : "yok")}", null);
    }
}
