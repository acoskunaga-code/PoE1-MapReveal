using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Harita simgelerinin kategorileri. Simge adlari ExileApi'nin MapIconsIndex listesinden geliyor
///     (<c>MapScan iconcatalog</c> bunu yerelde dosyaya yaziyor); kategori ad on ekinden seciliyor, ilk eslesen
///     kazaniyor. Kategori adlari arayuzde gorundugu icin Ingilizce. <see cref="Order"/> ayar sekmesindeki
///     sirayi belirliyor; liste disinda kalan kategori en sona ("Other") konuyor.
/// </summary>
internal static class IconCategories
{
    /// <summary>Lig mekanigi degil: loot filtresinin haritada gosterdigi renkli sekiller (396 tane). Sekmede yok.</summary>
    internal const string LootFilter = "Loot filter shapes";

    internal const string Other = "Other";

    internal static readonly string[] Order =
    {
        "Abyss", "Breach", "Essence", "Bestiary", "Incursion", "Delve", "Betrayal", "Synthesis", "Legion", "Blight",
        "Metamorph", "Delirium", "Harvest", "Heist", "Ritual", "Ultimatum", "Expedition", "Hellscape", "Royale",
        "Archnemesis", "Altars", "Sentinel", "Lake", "Sanctum", "Crucible", "Ancestral", "Warden",
        "Azmeri / Valdo / Necropolis", "Chayula", "Faridun / Mirage / Descension", "Deepwater / Brinerot",
        "Village / Racetrack / Mercenary / other", "Strongbox", "Corpse / Ore types", "Labyrinth",
        "Reward icons", "Reward chests", "World objects", "General",
    };

    private static readonly (Regex Pattern, string Name)[] Rules = new (string, string)[]
    {
        (@"^LootFilter", LootFilter),
        (@"^RewardChest", "Reward chests"),
        (@"^Reward", "Reward icons"),
        (@"^Abyss", "Abyss"),
        (@"^Bestiary", "Bestiary"),
        (@"^Incursion", "Incursion"),
        (@"^(Delve|DynamiteWall)", "Delve"),
        (@"^Betrayal", "Betrayal"),
        (@"^Synthesis", "Synthesis"),
        (@"^Legion", "Legion"),
        (@"^Blight", "Blight"),
        (@"^Metamorphosis", "Metamorph"),
        (@"^(Affliction|Delirium)", "Delirium"),
        (@"^Harvest", "Harvest"),
        (@"^Heist", "Heist"),
        (@"^Ritual", "Ritual"),
        (@"^Ultimatum", "Ultimatum"),
        (@"^Expedition", "Expedition"),
        (@"^Hellscape", "Hellscape"),
        (@"^Royale", "Royale"),
        (@"^Archnemesis", "Archnemesis"),
        (@"^(CleansingFire|Tangle)", "Altars"),
        (@"^Breach", "Breach"),
        (@"^Essence", "Essence"),
        (@"^Sentinel", "Sentinel"),
        (@"^Lake", "Lake"),
        (@"^Sanctum", "Sanctum"),
        (@"^Crucible", "Crucible"),
        (@"^Ancesl?tral", "Ancestral"),
        (@"^WardenTracker", "Warden"),
        (@"^(Azmeri|Valdo|Necropolis)", "Azmeri / Valdo / Necropolis"),
        (@"^Strongbox", "Strongbox"),
        (@"^(Corpse|Ore)Type", "Corpse / Ore types"),
        (@"^(Village|Racetrack|Mercenary|MemoryTear|Phrecian|Recombinator)", "Village / Racetrack / Mercenary / other"),
        (@"^Chayula", "Chayula"),
        (@"^(Faridun|Mirage|Descension)", "Faridun / Mirage / Descension"),
        (@"^(Deepwater|Brinerot|ChartStorage)", "Deepwater / Brinerot"),
        (@"^Labyrinth", "Labyrinth"),
        (@"^(Shrine|StoneCircle|VaalSideArea|StoryGlyph|WatchstoneAltar|FlaskFountain)", "World objects"),
        (@"^(None|MyPlayer|PartyMember|OtherPlayer|PartyLeader|NPC|Portal|EntrancePortal|Entrance|Quest|Mission|" +
         @"RedFlag|BlueFlag|GreenFlag|Waypoint|YellowWaypoint|Console|PCMapArrow|Crafting|MapDevice|Stash|HideoutSymbol)",
            "General"),
    }.Select(r => (new Regex(r.Item1, RegexOptions.Compiled), r.Item2)).ToArray();

    /// <summary>Simge adinin kategorisi; hicbir kurala uymayan "Other" olur.</summary>
    internal static string Of(string iconName)
    {
        foreach (var (pattern, name) in Rules)
        {
            if (pattern.IsMatch(iconName)) { return name; }
        }

        return Other;
    }

    /// <summary>"HarvestCollectorNeutral" -> "Harvest Collector Neutral".</summary>
    internal static string Pretty(string iconName) =>
        Regex.Replace(iconName, "(?<=[a-z0-9])(?=[A-Z])", " ");
}
