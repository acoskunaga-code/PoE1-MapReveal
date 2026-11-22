using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Poe2Map;

/// <summary>
///     Kullanici "Update now" dedikten sonra: zip indirilir, GitHub'in verdigi SHA-256 ile sinanir, icindeki tek exe
///     calisan exe'nin YANINA "&lt;ad&gt;.update.exe" olarak cikarilir ve surumu etiketle karsilastirilir. Degistirme isi
///     yeni exe'de (UpdateApply): calisan tek dosyali exe kendi dosyasinin yerini degistirmesin, kutuphanelerini o
///     dosyadan sonradan okuyor.
///
///     Gecici klasor, rastgele ad, gizli pencere, kabuk komutu yok. Hata olursa eski exe oldugu gibi kalir.
///     Hata yazilari ekrana ciktigi icin Ingilizce.
/// </summary>
internal static class UpdateInstall
{
    private const long MaxExeBytes = 400_000_000;

    /// <summary>
    ///     Kendini guncelleyebilir mi: yalniz yayinlanan tek dosyali exe. Gelistirme derlemesinde kutuphane exe'nin
    ///     yaninda ayri durur; o klasore tek dosyali exe konmaz.
    /// </summary>
    internal static bool CanInstall =>
        Environment.ProcessPath is { } exe && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        !File.Exists(Path.Combine(AppContext.BaseDirectory, typeof(UpdateInstall).Assembly.GetName().Name + ".dll"));

    internal static string HelperPath(string target) => Path.ChangeExtension(target, null) + ".update.exe";

    /// <summary>Indirir, sinar, yardimci exe'yi birakir ve yolunu dondurur.</summary>
    internal static async Task<string> PrepareAsync(UpdateFeed.Release release, string target, IProgress<int>? progress, CancellationToken cancel)
    {
        using var zip = await DownloadAsync(release, progress, cancel).ConfigureAwait(false);

        zip.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(zip, cancel).ConfigureAwait(false));
        if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded file is damaged (checksum mismatch).");
        }

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var programs = archive.Entries.Where(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        if (programs.Count != 1 || programs[0].Length is <= 0 or > MaxExeBytes)
        {
            throw new InvalidDataException("The downloaded file has no valid program in it.");
        }

        var helper = HelperPath(target);
        try
        {
            // Yalniz bu dosya yaziliyor; zip'in icindeki yollar kullanilmiyor.
            await using (var from = programs[0].Open())
            await using (var to = File.Create(helper))
            {
                await from.CopyToAsync(to, cancel).ConfigureAwait(false);
            }

            var info = FileVersionInfo.GetVersionInfo(helper);
            if (new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart) != release.Version)
            {
                throw new InvalidDataException("The downloaded program does not match the announced version.");
            }

            return helper;
        }
        catch
        {
            TryDelete(helper);
            throw;
        }
    }

    /// <summary>Yeni exe'yi "yerine gec" kipinde baslatir; cagiran hemen normal cikis yapar.</summary>
    internal static void StartHelper(string helper, string target)
    {
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) };
        start.ArgumentList.Add(UpdateApply.Argument);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(target);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The new version could not be started.");
    }

    private static async Task<MemoryStream> DownloadAsync(UpdateFeed.Release release, IProgress<int>? progress, CancellationToken cancel)
    {
        using var http = UpdateFeed.NewClient(TimeSpan.FromMinutes(10));
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{UpdateFeed.ApiRoot}repositories/{UpdateFeed.Repository}/releases/assets/{release.AssetId}");
        request.Headers.Accept.ParseAdd("application/octet-stream");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);

        var result = new MemoryStream((int)release.Size);
        var buffer = new byte[128 * 1024];
        var shown = -1;
        int read;

        // Zaman asimi govdeyi kapsamiyor (ResponseHeadersRead): takilan baglanti 60 sn'de kesilsin.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stall.CancelAfter(TimeSpan.FromSeconds(60));
        while ((read = await body.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
        {
            stall.CancelAfter(TimeSpan.FromSeconds(60));
            if (result.Length + read > release.Size) { throw new InvalidDataException("The download is larger than announced."); }

            result.Write(buffer, 0, read);
            var percent = (int)(result.Length * 100 / release.Size);
            if (percent == shown) { continue; }

            shown = percent;
            progress?.Report(percent);
        }

        return result.Length == release.Size ? result : throw new InvalidDataException("The download is incomplete.");
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
