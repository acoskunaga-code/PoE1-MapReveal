using System.Text.Json;

namespace Poe2Map;

/// <summary>
///     Unique resmi -> unique adi, istege bagli data\uniques.json'dan (RePoE'nin uniques.json'u oldugu gibi; depoda yok,
///     data/README.md). Her kayitta "name", "is_alternate_art" ve "visual_identity.dds_file". 18.11.2026 indirilen surum
///     1569 kayit, 3.30 unique'leri dahil (Linkmaster's Coil); Mageblood = Belts/InjectorBelt.dds, Headhunter =
///     Belts/Headhunter.dds (Replica Headhunter da ayni resim), alternatif resimler (Headhunter2.dds) ayri kayit.
///
///     Ayni resmi paylasan unique'ler (177 resim, or. Redbeak / Dreadbeak) "A / B" olarak donuyor: resimden hangisi
///     oldugu anlasilmiyor. Dosya yoksa ya da okunamazsa null: GroundUniqueReader resim adina duser.
/// </summary>
internal static class UniqueArt
{
    private static Dictionary<string, string>? names;

    /// <summary>Gunluk satiri (overlay CrashLog'a baglar; MapScan'de konsol yok sayilir).</summary>
    internal static Action<string, Exception?> Log = (_, _) => { };

    internal static string FileName => AppPaths.File("uniques.json");

    /// <summary>Dosya yeni indirildi (ayar penceresi, "Build list"): bir sonraki sorguda yeniden okunsun.</summary>
    internal static void Reload() => names = null;

    /// <summary>Resmin unique ad(lar)i; dosyada yoksa null.</summary>
    internal static string? Names(string art) => (names ??= Load()).TryGetValue(art, out var n) ? n : null;

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(FileName))
            {
                Log($"unique adlari: {FileName} yok, adlar resim dosyasindan", null);
                return map;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(FileName));
            var lists = new Dictionary<string, List<(string Name, bool Alt)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                var v = entry.Value;
                if (!v.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) { continue; }
                if (!v.TryGetProperty("visual_identity", out var vi) || vi.ValueKind != JsonValueKind.Object ||
                    !vi.TryGetProperty("dds_file", out var dds) || dds.ValueKind != JsonValueKind.String) { continue; }

                var alt = v.TryGetProperty("is_alternate_art", out var a) && a.ValueKind == JsonValueKind.True;
                var key = dds.GetString()!;
                if (!lists.TryGetValue(key, out var list)) { lists[key] = list = new(); }
                list.Add((name.GetString()!, alt));
            }

            foreach (var (art, list) in lists)
            {
                map[art] = string.Join(" / ", list.OrderBy(x => x.Alt).Select(x => x.Name).Distinct(StringComparer.Ordinal));
            }

            Log($"unique adlari: {map.Count} resim ({FileName})", null);
        }
        catch (Exception ex)
        {
            Log("unique adlari okunamadi", ex);
        }

        return map;
    }
}
