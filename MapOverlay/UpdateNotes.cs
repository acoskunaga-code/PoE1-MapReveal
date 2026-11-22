using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Guncelleme sorusunda gosterilen notlar (22.11.2026, kullanici: "guncelleme notlarinida gosterelim guncelleme var
///     istermisin dedigimiz zaman"): calisan surumden sonraki butun surumlerin "## Changes" bolumu, yeniden eskiye.
///     Duz yazi olarak gosteriliyor; notlardaki bicim isaretleri atilir, baglantilar calistirilmaz.
///
///     YAYIN: notlarda kullanicinin gormesi gereken degisiklikler "## Changes" basligi altinda olmali.
/// </summary>
internal static class UpdateNotes
{
    private const int MaxReleases = 20;
    private const int MaxPerRelease = 4000;

    private static readonly Regex Link = new(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex Emphasis = new(@"\*\*|__|`|(?<![\w*])\*(?=\S)|(?<=\S)\*(?![\w*])", RegexOptions.CultureInvariant);
    private static readonly Regex Bullet = new(@"^(\s*)[-*]\s+", RegexOptions.CultureInvariant);

    /// <summary>
    ///     "releases" listesinden <paramref name="current"/> ile <paramref name="latest"/> arasindakiler. Liste
    ///     okunamazsa (null) ya da bos cikarsa yalniz son surumun notu.
    /// </summary>
    internal static string Build(string? listJson, Version current, Version latest, string latestBody)
    {
        var found = new SortedDictionary<Version, string>();
        if (listJson is not null)
        {
            try { Read(listJson, current, latest, found); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                CrashLog.Write("surum notlari cozulemedi: " + ex.Message, null);
            }
        }

        found.TryAdd(latest, latestBody);
        var text = new StringBuilder();
        foreach (var (version, body) in found.Reverse())
        {
            if (text.Length > 0) { text.Append("\r\n\r\n"); }

            text.Append(UpdateFeed.Text(version)).Append("\r\n").Append(Plain(body));
        }

        return text.ToString();
    }

    private static void Read(string json, Version current, Version latest, SortedDictionary<Version, string> found)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var release in document.RootElement.EnumerateArray().Take(MaxReleases))
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) { continue; }
            if (release.GetProperty("author").GetProperty("id").GetInt64() != UpdateFeed.Owner) { continue; }
            if (!UpdateFeed.TryTag(release.GetProperty("tag_name").GetString(), out var version) || version <= current || version > latest) { continue; }

            found[version] = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        }
    }

    /// <summary>"## Changes" bolumu (yoksa notun tamami), bicim isaretsiz ve Windows satir sonlariyla.</summary>
    internal static string Plain(string body)
    {
        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var start = Array.FindIndex(lines, l => l.Trim().Equals("## Changes", StringComparison.OrdinalIgnoreCase));
        var section = start < 0 ? lines : lines.Skip(start + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal));

        var text = new StringBuilder();
        foreach (var raw in section)
        {
            var line = Link.Replace(raw.TrimEnd(), "$1").TrimStart('#');
            var indent = Bullet.Match(line);
            if (indent.Success) { line = $"{(indent.Groups[1].Length > 0 ? "      - " : "  • ")}{line[indent.Length..]}"; }

            line = Emphasis.Replace(line, "");
            if (line.Trim().Length == 0) { continue; }

            if (text.Length > 0) { text.Append("\r\n"); }

            text.Append(new string(line.Where(c => !char.IsControl(c)).ToArray()));
            if (text.Length > MaxPerRelease)
            {
                text.Length = MaxPerRelease;
                text.Append("...");
                break;
            }
        }

        return text.Length > 0 ? text.ToString() : "  (no notes)";
    }
}
