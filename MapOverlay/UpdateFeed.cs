using System.Net.Http;
using System.Text.Json;

namespace Poe2Map;

/// <summary>
///     Yeni surum var mi (22.11.2026, kullanici: "uygulamayi actiginda guncelleme var ise sorabilirmisin ... evet derse
///     otomatik githubdan guncelleyebilirmi"). GitHub'a sadece herkese acik bir GET; kimlik, ayar, hicbir veri gitmiyor.
///
///     Depo ve hesap ADIYLA degil NUMARASIYLA sabit: hesap silinir ya da askiya alinir da ayni adi baskasi alirsa numara
///     tutmaz, guncelleme onerilmez. Indirme adresi de cevaptan alinmiyor, ayni numaralardan kuruluyor.
///
///     Koruma siniri: hesabin kendisi ele gecirilirse bu sinavlar yetmez (imza anahtari yok).
///
///     YAYIN: MapOverlay.csproj'daki Version her surumde etiketle ayni yapilmali (v3.30.8 = 3.30.8); tutmazsa indirilen
///     exe reddedilir (UpdateInstall).
/// </summary>
internal static class UpdateFeed
{
    internal const long Repository = 1457195394;
    internal const long Owner = 356429273;
    internal const string ApiRoot = "https://api.github.com/";

    private const long MaxZipBytes = 150_000_000;

    internal sealed record Release(Version Version, long AssetId, long Size, string Sha256, string Page, string Notes);

    /// <summary>Calisan exe'nin surumu (csproj Version), uc parca.</summary>
    internal static Version Current { get; } = ThreeParts(typeof(UpdateFeed).Assembly.GetName().Version ?? new Version(0, 0));

    internal static string Text(Version version) => $"v{version.ToString(3)}";

    internal static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoE1-MapReveal");
        return http;
    }

    /// <summary>Daha yeni ve sinavlari gecen surum; yoksa null. Internet yoksa istisna (cagiran gunluge yazar).</summary>
    internal static async Task<Release?> CheckAsync()
    {
        using var http = NewClient(TimeSpan.FromSeconds(15));
        var json = await http.GetStringAsync($"{ApiRoot}repositories/{Repository}/releases/latest").ConfigureAwait(false);
        if (Parse(json, Current) is not { } release) { return null; }

        // Atlanan surumlerin notlari; liste okunamazsa soru yine sorulur, yalniz son surumun notuyla.
        string? list = null;
        try
        {
            list = await http.GetStringAsync($"{ApiRoot}repositories/{Repository}/releases?per_page=20").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            CrashLog.Write("surum notlari listesi okunamadi: " + ex.Message, null);
        }

        return release with { Notes = UpdateNotes.Build(list, Current, release.Version, release.Notes) };
    }

    /// <summary>"v3.30.8" -> 3.30.8.</summary>
    internal static bool TryTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var parsed)) { return false; }

        version = ThreeParts(parsed);
        return true;
    }

    /// <summary>GitHub'in "latest release" cevabi -> kurulabilir yeni surum ya da null.</summary>
    internal static Release? Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) { return null; }

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryTag(tag, out var version)) { return null; }
        if (version <= current || root.GetProperty("author").GetProperty("id").GetInt64() != Owner) { return null; }

        var wanted = $"PoE1-MapReveal-{tag}-win-x64.zip";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != wanted || asset.GetProperty("uploader").GetProperty("id").GetInt64() != Owner) { continue; }

            var size = asset.GetProperty("size").GetInt64();
            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
            if (size is <= 0 or > MaxZipBytes || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 7 + 64) { return null; }

            var page = root.GetProperty("html_url").GetString() ?? "";
            if (!page.StartsWith("https://github.com/", StringComparison.Ordinal)) { page = ""; }

            var body = root.TryGetProperty("body", out var text) ? text.GetString() ?? "" : "";
            return new Release(version, asset.GetProperty("id").GetInt64(), size, digest[7..], page, body);
        }

        return null;
    }

    internal static Version ThreeParts(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
