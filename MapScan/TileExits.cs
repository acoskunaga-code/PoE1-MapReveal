namespace Poe2Map;

/// <summary>
///     Alanin cikislarini zemin karolarindan cikarir ve bellekteki kapi varliklariyla birlestirir.
///
///     PoE1'de uzaktaki AreaTransition varliklari bellekte yok (sadece yakindakiler), karolar ise alan yuklenir
///     yuklenmez tamamen bellekte. Iki kaynak (kararin sirasi FromTiles'ta):
///       - Liste (ExitNames, Radar'in targets.json'i): alan kimligi + karo detay adi -> cikis mi, nereye gidiyor.
///       - Kurallar: yolunda "areatransition" gecen karo; adinda entrance / stairs / transition gecen detay.
///
///     Kapi varligi yaklasinca bellege yukleniyor (WithEntities); o zaman karo tahmininin yerine kapinin kesin konumu
///     ve oyunun kendi adi kullaniliyor.
/// </summary>
internal static class TileExits
{
    /// <summary>
    ///     Region: karo tabanli cikislarda karo kumesinin dunya birimindeki sinirlari; yol hesabi tohumu bu bolgeden
    ///     aliyor (FlowField.Build). Kapi varliklarinda yok: konum kesin.
    /// </summary>
    internal readonly record struct Target(
        string Key, string Name, float X, float Y,
        (float MinX, float MinY, float MaxX, float MaxY)? Region = null);

    /// <summary>Karo kumesinin sinirlari: karo merkezlerinin kapsadigi alan + yarim karo (125 birim) pay.</summary>
    private static (float MinX, float MinY, float MaxX, float MaxY) RegionOf(List<TileReader.Tile> cluster)
    {
        const float Half = TerrainFinder.TileToWorld / 2f;
        var centers = cluster.Select(TileReader.Center).ToList();
        return (centers.Min(c => c.X) - Half, centers.Min(c => c.Y) - Half,
                centers.Max(c => c.X) + Half, centers.Max(c => c.Y) + Half);
    }

    private const float MergeDistance = 1000f;
    private const float EntitySnapDistance = 900f;

    internal static List<Target> FromTiles(List<TileReader.Tile> tiles, string? areaId = null) =>
        FromTiles(tiles, areaId, out _);

    /// <summary>
    ///     areaId: Radar alan kimligi (AreaIdentity.RadarId). report: hangi kaynaktan ne bulundugu, tek satir (gunluge).
    ///
    ///     Karar sirasi, (karo yolu, detay adi) cifti basina:
    ///       1. Liste (ExitNames.Classify): kimlik + detay listede "cikis" ise hedef, "cikis degil" ise (ugrak, sandik, NPC)
    ///          hicbir kural onu cikis yapmaz. Detay adlari serbest metin ("top", "exit", "cave"); adina bakarak karar
    ///          vermek Vaal City'de Causeway cikisini kacirtti.
    ///       2. Yol kurali: yolunda "areatransition" gecen karo.
    ///       3. Detay kurali: adinda entrance / stairsup / stairsdown / transition gecen detay ("notransition" haric).
    ///     Liste alani bilmiyorsa (kampanya disi, kimlik okunamadi) 2 ve 3 tek basina calisir.
    /// </summary>
    internal static List<Target> FromTiles(List<TileReader.Tile> tiles, string? areaId, out string report)
    {
        var listed = new List<Target>();
        var ruled = new List<Target>();
        var listedNotes = new List<string>();
        var ruledNotes = new List<string>();
        var presentDetails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in tiles.GroupBy(t => (t.Path, t.Detail)))
        {
            var (path, detail) = group.Key;
            if (detail.Length > 0) { presentDetails.Add(detail); }

            var verdict = ExitNames.Classify(areaId, detail, path);
            if (verdict is { IsExit: false }) { continue; }

            string name;
            var fromList = verdict is { IsExit: true };
            if (fromList) { name = verdict!.Value.Display ?? (detail.Length > 0 ? PrettyDetail(detail) : PrettyName(path)); }
            else if (IsGenericExit(path)) { name = PrettyName(path); }
            else if (detail.Length > 0 && IsTransitionFolder(path)) { name = TransitionName(path); }
            else if (IsExitDetail(detail)) { name = PrettyDetail(detail); }
            else { continue; }

            var clusters = Clusters(group.ToList());
            foreach (var cluster in clusters)
            {
                var x = cluster.Average(t => TileReader.Center(t).X);
                var y = cluster.Average(t => TileReader.Center(t).Y);
                (fromList ? listed : ruled).Add(new Target(
                    $"karo:{path}@{cluster[0].TileX},{cluster[0].TileY}", name, x, y, RegionOf(cluster)));
            }

            (fromList ? listedNotes : ruledNotes).Add($"{(detail.Length > 0 ? detail : "yol")}->{name} ({group.Count()} karo, {clusters.Count} kume)");
        }

        // Ayni kapiya ait yakin girisleri tekillestir (listede ayni kapi icin iki alt karo olabiliyor). Listeden gelenler
        // once: adli olan kalir. Iki FARKLI adli liste girdisi yakin olsa da ayri kapi sayilir.
        var merged = new List<Target>();
        foreach (var t in listed.Concat(ruled))
        {
            var isListed = listed.Contains(t);
            if (merged.Any(m => Distance(m.X, m.Y, t.X, t.Y) < MergeDistance &&
                                (!isListed || !listed.Contains(m) || m.Name == t.Name)))
            {
                continue;
            }

            merged.Add(t);
        }

        // Listenin bu alan icin bildigi ama karolarda bulunamayan cikislar: ad degismis ya da okuma eksik demek.
        var missing = ExitNames.ExitsOf(areaId)
            .Where(e => !e.Detail.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase) && !presentDetails.Contains(e.Detail))
            .Select(e => e.Detail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        report = $"liste [{string.Join("; ", listedNotes)}]  kural [{string.Join("; ", ruledNotes)}]  " +
                 $"listede olup karoda yok [{string.Join(", ", missing)}]  -> {merged.Count} hedef";
        return merged;
    }

    /// <summary>Bellekteki kapi varliklari: yakin bir karo cikisi varsa onun konumunu duzeltir, yoksa ekler.</summary>
    internal static List<Target> WithEntities(List<Target> fromTiles, List<ExitFinder.Exit> entities)
    {
        var result = new List<Target>(fromTiles);

        foreach (var e in entities)
        {
            var nearest = -1;
            var best = EntitySnapDistance;
            for (var i = 0; i < result.Count; i++)
            {
                var d = Distance(result[i].X, result[i].Y, e.X, e.Y);
                if (d < best) { best = d; nearest = i; }
            }

            if (nearest >= 0)
            {
                // Kapi bellekteyse adi da oyunun kendisinden: karo adi (act8_temple_entrance...) yerine gidilen alanin adi.
                result[nearest] = result[nearest] with { Name = e.Name, X = e.X, Y = e.Y, Region = null };
            }
            else
            {
                result.Add(new Target($"varlik:{e.Id}", e.Name, e.X, e.Y));
            }
        }

        return result;
    }

    /// <summary>
    ///     PoE1: oyun gecis karolarini AreaTransitions klasorune koyuyor (RuinedCity/AreaTransitions/act8_temple_entrance_v01_01,
    ///     mine_areatransition_v04_01...); ic merdivenler (slum_thinwall_stairs, mine_stairs_*) baska klasorlerde.
    ///     Eski genel kural (yolunda transition / entrance / stairs) ic merdivenleri de cikis sayiyordu.
    /// </summary>
    private static bool IsGenericExit(string path) =>
        path.Contains("areatransition", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     13.11.2026 (The Coast, canli `MapScan tilelist`): kampanya gecisleri ".../Beach/Transitions/" klasorunde ve detay adlari
    ///     serbest ("beachtownnorth", "karui"): beachtown_north_entrance_v2, TidalIsland/act1_karui_coast_to_island_transition,
    ///     BeachToSwamp/act1_area2_transition. Liste olmayan surumde (ve alan kimligi okunamayinca) uzak cikislari bu kural
    ///     buluyor. Detay adi sart: tasarimcinin isaretledigi karo; adsiz zemin gecis karolari sayilmiyor.
    /// </summary>
    private static bool IsTransitionFolder(string path) =>
        path.Contains("/transitions/", StringComparison.OrdinalIgnoreCase);

    /// <summary>".../Transitions/TidalIsland/act1_..." -> "Tidal Island"; alt klasor yoksa dosya adindan (PrettyName).</summary>
    private static string TransitionName(string path)
    {
        var at = path.IndexOf("/transitions/", StringComparison.OrdinalIgnoreCase) + "/transitions/".Length;
        var rest = path[at..];
        var slash = rest.IndexOf('/');
        if (slash <= 0) { return PrettyName(path); }

        var folder = rest[..slash];
        return System.Text.RegularExpressions.Regex.Replace(folder, "(?<=[a-z])(?=[A-Z])", " ");
    }

    /// <summary>TGT detay adi bir gecis noktasi mi: ...entrance..., stairsup / stairsdown, ...transition...</summary>
    private static bool IsExitDetail(string detail) =>
        !detail.Contains("notransition", StringComparison.OrdinalIgnoreCase) && // road_wall_gate_notransition: "gecis YOK" susu
        (detail.Contains("entrance", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("stairsup", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("stairsdown", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("transition", StringComparison.OrdinalIgnoreCase));

    /// <summary>"templeentrance" -> "temple entrance", "stairsdown" -> "stairs down".</summary>
    private static string PrettyDetail(string detail)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(detail, "(entrance|stairs|transition)", " $1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        spaced = System.Text.RegularExpressions.Regex.Replace(spaced, "(up|down)$", " $1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(spaced.Replace('_', ' ').Trim(), @"\s+", " ");
    }

    /// <summary>Ayni dosyanin karolarini komsuluga gore ayri kapilara bolur (bir alanda iki kez gecebilir).</summary>
    private static List<List<TileReader.Tile>> Clusters(List<TileReader.Tile> tiles)
    {
        var clusters = new List<List<TileReader.Tile>>();
        var left = new List<TileReader.Tile>(tiles);

        while (left.Count > 0)
        {
            var cluster = new List<TileReader.Tile> { left[0] };
            left.RemoveAt(0);

            for (var i = 0; i < cluster.Count; i++)
            {
                for (var j = left.Count - 1; j >= 0; j--)
                {
                    if (Math.Abs(left[j].TileX - cluster[i].TileX) <= 1 && Math.Abs(left[j].TileY - cluster[i].TileY) <= 1)
                    {
                        cluster.Add(left[j]);
                        left.RemoveAt(j);
                    }
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    /// <summary>".../JungleDepths_to_ChimeralWetlands.tdt" -> "JungleDepths_to_ChimeralWetlands".</summary>
    private static string PrettyName(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        name = dot > 0 ? name[..dot] : name;

        // "act8_temple_entrance_v01_01" -> "act8 temple entrance": surum soneki atilir, alt cizgiler bosluk olur.
        name = System.Text.RegularExpressions.Regex.Replace(name, @"(_v\d+)?(_\d+)*$", "");
        return name.Replace('_', ' ');
    }

    private static float Distance(float ax, float ay, float bx, float by) =>
        MathF.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));
}
