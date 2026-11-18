using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Uniques sekmesinin listesi (data\unique-tiers.txt): her unique'in taban esyasi poewiki'den (Cargo API, items
///     tablosu, rarity Unique, yalniz dusebilenler), grubu kullanicinin loot filtresinden. Filtre yoksa gruplar esya
///     sinifina gore (Belt, Ring ...). Ayni anda RePoE'nin uniques.json'u da yoksa indiriliyor (yerdeki unique'in tam adi,
///     UniqueArt). Ikisi de ucuncu taraf verisi: depoda ve surum zip'inde yok, kullanicinin bilgisayarinda uretiliyor
///     (ayar penceresindeki "Build list" dugmesi, gelistirmede `MapScan uniquetiers`). Kullanici istegi 18.11.2026.
/// </summary>
internal static class UniqueTierBuilder
{
    internal const string RePoEUniques = "https://repoe-fork.github.io/uniques.json";

    private const string Api = "https://www.poewiki.net/w/api.php?action=cargoquery&tables=items" +
                               "&fields=items.name,items.base_item,items.class,items.drop_enabled" +
                               "&where=items.rarity%3D%22Unique%22&format=json&limit=500&offset=";

    private const string OtherGroup = "other";

    // Tanimlanmamis, kosulsuz bir unique'e uymayan kosullar: bunlarin oldugu kural o esyayi yakalamaz.
    private static readonly Regex Special = new(
        "Sockets|Corrupted|Influence|Synthesised|Replica|Foulborn|Fractured|Mirrored|Quality|Vestigial|Crucible|Identified|" +
        "MapTier|ItemLevel|AreaLevel|Enchanted|Scourged|Transfigured|GemLevel|HasImplicit|Memory|Class ==");

    // FilterBlade'deki adlar (kullanici 18.11.2026: "s tier olarak geciyor. soul taker var mesela"): NeverSink t1 kuralinin
    // stili "!apex_sunique" = S, t2 "!uniques_a" = A, t3 "!uniques_b" = B, hideable "!uniques_c" = C.
    private static readonly Dictionary<string, string> TierTitles = new(StringComparer.Ordinal)
    {
        ["t1"] = "S tier", ["t2"] = "A tier", ["multispecialhigh"] = "Multi special high", ["multispecial"] = "Multi special",
        ["earlyleague"] = "Early league", ["exjewels"] = "Jewels", ["t3boss"] = "Boss drops", ["t3"] = "B tier",
        ["hideable2"] = "C tier 2", ["hideable"] = "C tier",
    };

    /// <summary>
    ///     Listeyi uretir ve <paramref name="output"/>'a yazar; filtre null ise gruplar esya sinifina gore. Doner: gunluk /
    ///     ekran icin satirlar (grup basina sayi). Ag ya da dosya hatasi istisna olarak cikar.
    /// </summary>
    internal static List<string> Build(string? filterPath, string output)
    {
        var report = new List<string>();
        var uniques = DownloadUniques();
        report.Add($"poewiki: {uniques.Count} droppable uniques");

        List<(string Id, string Title, bool Show)> groups;
        Func<(string Name, string Base, string Class), string> groupOf;
        if (filterPath is not null)
        {
            var (fromFilter, byBase) = ReadFilterGroups(File.ReadAllLines(filterPath));
            groups = fromFilter.Append((Id: OtherGroup, Title: "Other (shown by the filter's last unique rule)", Show: true)).ToList();
            groupOf = u => byBase.GetValueOrDefault(u.Base, OtherGroup);
        }
        else
        {
            groups = uniques.Select(u => u.Class).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).Select(c => (Id: "class:" + c, Title: c, Show: true)).ToList();
            groups.Add((OtherGroup, "Other", true));
            groupOf = u => u.Class.Length > 0 ? "class:" + u.Class : OtherGroup;
        }

        var lines = new List<string>
        {
            "# Uniques sekmesi. '@grup' satirlari sirayla: kimlik<TAB>baslik<TAB>show/hide. Sonra her unique: grup<TAB>ad<TAB>taban<TAB>sinif.",
            "# Grup = " + (filterPath is null ? "esya sinifi (filtre secilmedi)" : $"tanimlanmamis, kosulsuz bir unique'e {Path.GetFileName(filterPath)}'da ilk uyan kural") +
            "; taban poewiki'den. Ayar penceresi, Uniques sekmesi, 'Build list' ile yeniden uretilir.",
        };
        lines.AddRange(groups.Select(g => $"@grup\t{g.Id}\t{g.Title}\t{(g.Show ? "show" : "hide")}"));
        foreach (var u in uniques.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{groupOf(u)}\t{u.Name}\t{u.Base}\t{u.Class}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllLines(output, lines);
        foreach (var (id, title, _) in groups)
        {
            var count = uniques.Count(u => groupOf(u) == id);
            if (count > 0) { report.Add($"{count,4}  {title}"); }
        }

        return report;
    }

    /// <summary>RePoE uniques.json'u indirir (yerdeki unique'in resminden tam adi). Basariliysa true.</summary>
    internal static bool DownloadRePoE(string path)
    {
        using var http = NewClient();
        var text = http.GetStringAsync(RePoEUniques).GetAwaiter().GetResult();
        using (JsonDocument.Parse(text)) { }   // bozuk / yarim indirme diske yazilmasin

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text);
        return true;
    }

    /// <summary>
    ///     Filtredeki, yalnizca "Rarity Unique" + BaseType kosullu etkin kurallar sirayla (oyun ilk uyani uyguluyor):
    ///     NeverSink'in "$type->uniques $tier->X" kurallari ve FilterBlade'in "Conditional Entry" kurallari (gelistirmede
    ///     denenen filtrede Leather Belt / Heavy Belt / Paua Amulet ozel bir kuraldaydi, NeverSink tier'inda degil). Taban -> ilk kural.
    /// </summary>
    internal static (List<(string Id, string Title, bool Show)> Groups, Dictionary<string, string> ByBase) ReadFilterGroups(string[] filter)
    {
        var groups = new List<(string Id, string Title, bool Show)>();
        var byBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var custom = 0;
        for (var i = 0; i < filter.Length; i++)
        {
            var head = Regex.Match(filter[i], @"^(Show|Hide)\b(.*)");
            if (!head.Success) { continue; }

            var conditions = new List<string>();
            var bases = new List<string>();
            for (var j = i + 1; j < filter.Length && filter[j].Trim().Length > 0 && !Regex.IsMatch(filter[j], @"^(Show|Hide)\b"); j++)
            {
                var line = filter[j].Trim();
                if (line.StartsWith("BaseType", StringComparison.Ordinal))
                {
                    bases.AddRange(Regex.Matches(line, "\"([^\"]+)\"").Select(b => b.Groups[1].Value));
                }
                else if (!Regex.IsMatch(line, "^(Set|Play|Minimap|Custom|Continue|DisableDrop|#)")) { conditions.Add(line); }
            }

            var rule = string.Join(" ; ", conditions);
            if (bases.Count == 0 || !Regex.IsMatch(rule, @"Rarity.*\bUnique\b") || Special.IsMatch(rule)) { continue; }

            var show = head.Groups[1].Value == "Show";
            var tier = Regex.Match(head.Groups[2].Value, @"\$type->uniques \$tier->(\S+)");
            string id, title;
            if (tier.Success)
            {
                id = tier.Groups[1].Value;
                title = TierTitles.GetValueOrDefault(id, id);
            }
            else
            {
                custom++;
                id = $"custom{custom}";
                title = $"Your filter rule {custom}" + (bases.Count <= 3 ? $": {string.Join(", ", bases)}" : $" ({bases.Count} bases)");
            }

            if (!show) { title += " (hidden by the filter)"; }
            if (groups.Any(g => g.Id == id)) { continue; }

            groups.Add((id, title, show));
            foreach (var b in bases) { byBase.TryAdd(b, id); }
        }

        return (groups, byBase);
    }

    private static List<(string Name, string Base, string Class)> DownloadUniques()
    {
        using var http = NewClient();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<(string, string, string)>();

        for (var offset = 0; offset < 20_000; offset += 500)
        {
            using var doc = JsonDocument.Parse(http.GetStringAsync(Api + offset).GetAwaiter().GetResult());
            var rows = doc.RootElement.GetProperty("cargoquery");
            if (rows.GetArrayLength() == 0) { break; }

            foreach (var row in rows.EnumerateArray())
            {
                var t = row.GetProperty("title");
                var name = Field(t, "name");
                var baseItem = Field(t, "base item");
                if (name.Length == 0 || baseItem.Length == 0 || Field(t, "drop enabled") != "1") { continue; }
                if (seen.Add(name)) { list.Add((name, baseItem, Field(t, "class"))); }
            }
        }

        return list;
    }

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("map-overlay-tool/1.0");
        return http;
    }

    private static string Field(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(v.GetString()!).Trim() : "";
}
