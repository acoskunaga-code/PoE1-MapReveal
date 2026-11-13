using System.Text.Json;

namespace Poe2Map;

/// <summary>
///     Uzak cikislarin gidilen alan adi VE hangi karonun cikis oldugu. Oyun bunu uzaktaki kapilar icin bellekte
///     tutmuyor; ama karo detay adi + alan kimligi ("2_7_11") yeterli: Radar'in elle hazirlanmis listesi
///     (targets.json) tam bunu soyluyor, ornegin 2_7_11: top -> "The Causeway", exit -> "The Temple of Decay Level 1".
///
///     Detay adlari tasarimcilarin serbest metni: "top", "exit", "cave", "groveentrance", "transition"... Adinda
///     entrance / stairs / transition gecmesine GUVENILEMEZ (Vaal City'nin iki cikisi da boyle degil). Bu yuzden liste
///     tek basina cikis KARARI veriyor: kimlik + detay listede varsa ve girdi bir cikis ise hedeftir.
///
///     Liste ucuncu taraf VERISI (ofset degil), yerel ipucu: exe'nin yanindaki data\radar-targets.json ya da
///     <exe klasoru>\data\ (AppPaths)\radar-targets.json; yoksa ya da eslesme yoksa cagiran genel kurala duser. Dagitima konmaz.
/// </summary>
internal static class ExitNames
{
    /// <summary>
    ///     Hicbir sey soylemeyen adlar: cikis ama ne tur oldugu bile belli degil; cagiran karo detayindan uretilen adi kullanir.
    ///     "Exit", "Stairs Up", "Door" gibi adlar kalir: gidilen yeri soylemese de ne olduklarini soyluyorlar.
    /// </summary>
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "Area Transition", "Area Transitions", "Transition", "Both Transitions", "Area Transition (50-50)",
        "Near Real Area Transition",
    };

    /// <summary>
    ///     Listede cikis OLMAYAN girdiler (Radar bunlari "onemli yer" diye tutuyor): gorev nesneleri, sandiklar, NPC'ler,
    ///     boss adlari, gizli kapilar. Tam ad eslesmesi: "Oak" gibi kisa adlar baska adlarin icinde gecmesin. Liste
    ///     dondurulmus bir kopya oldugu icin kume tamam; yeni bir girdi eklenirse yalnizca fazladan bir isaret belirir.
    /// </summary>
    private static readonly HashSet<string> NotAnExitNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Waypoint", "Altar", "Piety", "Fidelitas", "Gems", "Kraityn", "Alira", "Oak", "Skillpoint", "Ancient Seal", "Platinum Bust",
        "Decanter Spiritus", "Blackguard Chest", "Supply Container", "Supply Host", "Lady Dialla", "General Gravicious",
        "The Sceptre of God", "Plum", "Siosa", "Book Stand", "Dominus", "Unique Boss", "Unique Monster", "Miasmeter", "Boss",
        "Riptide", "Hailrake", "Allflame", "Captain Fairgraves", "Flag Chest", "Beacon", "Weylam", "Dirty Lockbox", "Map Device",
        "Azmeri Shrine", "Mid-area", "Flask Strongbox", "Rhoa Nest", "Pressure Plate", "Statue", "Secret door", "Heist target",
        "Boss stronghold", "Exit Maze",
    };

    /// <summary>Adinda bunlar gecen girdiler cikis degil (gorev esyalari, sandiklar, lig isaretleri "[Wild/Purple]").</summary>
    private static readonly string[] NotAnExitWords = { "Quest", "Strongbox", "Lockbox", "[" };

    private static Dictionary<string, List<(string Name, string Display)>>? list;

    /// <summary>
    ///     Alan kimligi ve karo detay adi icin listedeki girdi: (cikis mi, gidilen alanin adi). Girdi yoksa null.
    ///     Ad genel ise ("Exit", "Transition") cikis=true ve Display=null: cikis, adi belli degil.
    /// </summary>
    internal static (bool IsExit, string? Display)? Classify(string? areaId, string detail, string path = "")
    {
        if (string.IsNullOrEmpty(detail) && string.IsNullOrEmpty(path)) { return null; }

        var map = List;
        foreach (var key in Keys(areaId))
        {
            if (!map.TryGetValue(key, out var entries)) { continue; }

            foreach (var (name, display) in entries)
            {
                // Girdi adi ya karo detayi ("top") ya da tam karo yolu ("Metadata/Terrain/.../x.tdt") olabiliyor.
                var matches = name.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)
                    ? name.Equals(path, StringComparison.OrdinalIgnoreCase)
                    : name.Equals(detail, StringComparison.OrdinalIgnoreCase);
                if (!matches) { continue; }

                if (display.Length == 0 || NotAnExitNames.Contains(display) ||
                    NotAnExitWords.Any(w => display.Contains(w, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, null);
                }

                return (true, Generic.Contains(display) || display.Equals(name, StringComparison.OrdinalIgnoreCase) ? null : display);
            }
        }

        return null;
    }

    /// <summary>Alan kimligi ve karo detay adi icin gidilen alanin adi; yoksa null (eski arayuz, adlandirma icin).</summary>
    internal static string? Lookup(string? areaId, string detail) =>
        Classify(areaId, detail) is { IsExit: true, Display: { } display } ? display : null;

    /// <summary>
    ///     Listede alan kimligi belli, o alan icin girdi var mi: varsa o alanin cikislarini liste belirler, yoksa genel kural.
    /// </summary>
    internal static bool HasArea(string? areaId) => areaId is not null && Keys(areaId).Any(List.ContainsKey);

    /// <summary>Listenin butun girdileri (MapScan exitlist).</summary>
    internal static IEnumerable<(string Id, string Detail, string Display)> AllEntries() =>
        List.SelectMany(a => a.Value.Select(e => (a.Key, e.Name, e.Display)));

    /// <summary>Alanin listedeki cikis detay adlari (tani gunlugu icin).</summary>
    internal static IEnumerable<(string Detail, string Display)> ExitsOf(string? areaId)
    {
        foreach (var key in Keys(areaId))
        {
            if (!List.TryGetValue(key, out var entries)) { continue; }

            foreach (var (name, display) in entries)
            {
                if (Classify(areaId, name, name) is { IsExit: true }) { yield return (name, display); }
            }
        }
    }

    /// <summary>Kimlik once kendi basina, sonra genel ("*") girdiler; kimlik yoksa yalnizca genel.</summary>
    private static IEnumerable<string> Keys(string? areaId)
    {
        if (areaId is not null) { yield return areaId; }
        yield return "*";
    }

    private static Dictionary<string, List<(string Name, string Display)>> List
    {
        get
        {
            if (list is not null) { return list; }

            var map = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Candidates())
            {
                try
                {
                    if (!File.Exists(path)) { continue; }

                    using var json = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var area in json.RootElement.EnumerateObject())
                    {
                        if (area.Value.ValueKind != JsonValueKind.Array) { continue; }

                        var entries = new List<(string, string)>();
                        foreach (var t in area.Value.EnumerateArray())
                        {
                            var name = t.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                            var display = t.TryGetProperty("DisplayName", out var d) ? d.GetString() ?? "" : "";
                            if (name.Length > 0) { entries.Add((name, display)); }
                        }

                        // Listede "2_8_7_1_" gibi sonda alt cizgi kalmis anahtarlar var (yazim hatasi); kimlik ayni.
                        var id = area.Name.TrimEnd('_');
                        if (map.TryGetValue(id, out var existing)) { existing.AddRange(entries); }
                        else { map[id] = entries; }
                    }

                    break;
                }
                catch
                {
                    // Bozuk liste isimleri bozmasin; karo detayindan uretilen ad kullanilir.
                }
            }

            list = map;
            return map;
        }
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "data", "radar-targets.json");
        yield return Path.Combine(AppContext.BaseDirectory, "radar-targets.json");
        yield return AppPaths.File("radar-targets.json");
    }
}
