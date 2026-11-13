using System.Diagnostics;
using System.Net.Http;

namespace Poe2Map;

/// <summary>
///     Simge verisi dagitilan surumde yok. Iki parca, ikisi de kendiliginden:
///
///         Icons.png   exApiTools/ExileApi-Compiled textures/Icons.png -> &lt;exe klasoru&gt;\data\ (AppPaths)\icons\Icons.png
///                     ilk acilista (lisanssiz ucuncu taraf resim; yeniden dagitmiyoruz, orijinal depodan indiriliyor)
///         catalog.txt simge adlari, OYUNUN KENDI tablosundan (IconTable, OverlayContext.Icons.cs): oyun bir alandayken
///                     bir kez okunup yaziliyor. Eskiden ExileCore.dll indirilip metadata'sindan okunuyordu; 14.11.2026'dan
///                     beri disaridan hicbir program dosyasi indirilmiyor (oyunun tablosu resimle ayni sirada, 699/699).
///
///     Indirme basarisizsa (internet yok, depo tasindi) gunluge yaziliyor ve bir sonraki acilista yeniden deneniyor; harita
///     bundan etkilenmiyor, sadece simgeler cizilmiyor.
/// </summary>
internal static class IconDownload
{
    private const string Source = "https://raw.githubusercontent.com/exApiTools/ExileApi-Compiled/master/";

    internal static string Folder => AppPaths.File("icons");

    internal static string CatalogFile => Path.Combine(Folder, "catalog.txt");

    /// <summary>true: resim yeni indirildi, katalog yeniden yuklenmeli. false: zaten vardi ya da indirilemedi.</summary>
    internal static async Task<bool> EnsureAsync()
    {
        var atlas = Path.Combine(Folder, "Icons.png");
        if (File.Exists(atlas)) { return false; }

        var sw = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(Folder);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PoE1-MapReveal");

            var png = await http.GetByteArrayAsync(Source + "textures/Icons.png").ConfigureAwait(false);
            if (png.Length < 8 || png[0] != 0x89 || png[1] != (byte)'P' || png[2] != (byte)'N' || png[3] != (byte)'G')
            {
                throw new InvalidDataException("Icons.png bir PNG degil");
            }

            await File.WriteAllBytesAsync(atlas + ".part", png).ConfigureAwait(false);
            File.Move(atlas + ".part", atlas, overwrite: true);
            CrashLog.Write($"simge resmi indirildi: {png.Length / 1024} KB, {sw.ElapsedMilliseconds} ms -> {Folder}", null);
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"simge resmi indirilemedi ({sw.ElapsedMilliseconds} ms): {ex.Message} - sonraki acilista yeniden denenecek", null);
            return false;
        }
    }
}
