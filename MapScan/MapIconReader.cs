using System.Text;

namespace Poe2Map;

/// <summary>
///     Harita simgesi: varligin MinimapIcon bileseninden simge adini okur. Adlar ExileCore'un MapIconsIndex
///     listesindeki adlarla ayni (NPC, Waypoint, StashPlayer, ...), ayar penceresindeki Icons sekmesi bu adlarla
///     calisiyor.
///
///     PoE1 (12.11.2026, canli, MapScan minimapicons):
///
///         bilesen +0x08   sahip varlik (OwnedBy)
///         bilesen +0x20   MinimapIcons.dat satirina isaretci; satirlar 34 bayt aralikli, yani adres HIZALI DEGIL
///         satir   +0x00   Id'nin UTF-16 metnine isaretci (NPC, Waypoint, MapDevice, ...)
///         bilesen +0x28   dat dosyasi nesnesi (+0x08'de "Data/MinimapIcons.dat")
///         bilesen +0x3C   gizli: 0 -> 1 olunca oyun simgeyi kaldiriyor (acilan strongbox, acilmis Crafting Recipe;
///                         12.11.2026 MapScan minimapicons-watch: Blacksmith's Strongbox acildigi anda 0 -> 1, ayni
///                         anda Chest +0x40 sifirlandi ve StateMachine +0x10 0 -> 1 oldu)
///         Render  +0x148  gorunen ad (std::wstring): "Lightning Shrine", "Armourer's Strongbox", "The Envoy"
///
///     Kanit: satir adreslerinin farki satir sirasini veriyor ve ExileCore enum degeriyle birebir tutuyor
///     (NPC 4, Waypoint 10, CraftingBench 163, StashPlayer 169, StashGuild 170, SanctumRelicLocker 607): simge
///     indeksi = dat satir sirasi. Bu yuzden adi katalogda aramak yeterli. Yamadan sonra `MapScan minimapicons`
///     ayni tabloyu yeniden basar; +0x20 kayarsa satir sutunu "?" olur. Ad durumla degisebiliyor (bkz. ReadName).
/// </summary>
internal static class MapIconReader
{
    internal const string ComponentName = "MinimapIcon";

    /// <summary>Bilesendeki dat satiri isaretcisi.</summary>
    internal static ulong RowPointer = 0x20;

    /// <summary>Bilesendeki gizli bayragi (int, 0 = gorunur).</summary>
    internal static ulong HiddenFlag = 0x3C;

    /// <summary>
    ///     Shrine bileseni +0x24 (int): alininca 1. Shrine'larda MinimapIcon +0x3C alininca DEGISMIYOR, bu yuzden ayri.
    ///     12.11.2026 canli (MapScan minimapicons-detail Shrine): alinan Shrouded Shrine ve Lightning Shrine 1, ayni
    ///     haritada alinmamis Impenetrable ve Acceleration Shrine 0.
    /// </summary>
    internal static ulong ShrineTaken = 0x24;

    private const int MaxNameLength = 64;

    // Dat tablosu oyun boyunca yerinde duruyor: satir adresi -> Id.
    private static readonly Dictionary<ulong, string> Names = new();

    internal readonly record struct IconState(string Name, bool Hidden);

    /// <summary>
    ///     Simgenin o anki adi ve gizli olup olmadigi; simgesi yoksa ya da okunamadiysa null. Ad varliga gore
    ///     SABIT DEGIL: oyun durum degisince bilesenin satirini degistiriyor (Reef, 12.11.2026: ayni AbyssCrack
    ///     varligi once AbyssCrackInactive, tetiklenince AbyssCrackActive). O yuzden bilesen her cagrida okunuyor
    ///     (tek okuma, 0x40 bayt); satir -> ad onbellekte. Sahiplik sinavi varlik basina bir kez, en fazla uc deneme.
    /// </summary>
    internal static IconState? Read(IntPtr h, ulong entity, EntityReader.EntityInfo info)
    {
        if (!info.Components.TryGetValue(ComponentName, out var component)) { return null; }

        if (!info.IconOwned)
        {
            if (info.IconChecks >= 3) { return null; }

            info.IconChecks++;
            if (!EntityReader.OwnedBy(h, component, entity)) { return null; }

            info.IconOwned = true;
        }

        var buf = new byte[(int)HiddenFlag + 4];
        if (!EntityReader.Read(h, component, buf)) { return null; }

        var name = RowName(h, BitConverter.ToUInt64(buf, (int)RowPointer));
        if (name is null) { return null; }

        var hidden = BitConverter.ToInt32(buf, (int)HiddenFlag) != 0 || Taken(h, info);
        return new IconState(name, hidden);
    }

    /// <summary>Kullanilmis mi: alinmis shrine (Shrine +0x24). Strongbox'lar MinimapIcon +0x3C ile zaten gizleniyor.</summary>
    private static bool Taken(IntPtr h, EntityReader.EntityInfo info)
    {
        if (!info.Components.TryGetValue("Shrine", out var shrine)) { return false; }

        var flag = new byte[4];
        return EntityReader.Read(h, shrine + ShrineTaken, flag) && BitConverter.ToInt32(flag, 0) != 0;
    }

    /// <summary>
    ///     Varligin oyundaki gorunen adi (Render +0x148): shrine turu, strongbox turu, NPC adi. Degismiyor;
    ///     varlik basina bir kez okunup varlik bilgisinde tutuluyor. Adi olmayan varlikta "".
    /// </summary>
    internal static string DisplayName(IntPtr h, EntityReader.EntityInfo info)
    {
        if (info.DisplayName is { } known) { return known; }
        if (!info.Components.TryGetValue("Render", out var render)) { return info.DisplayName = ""; }

        var buf = new byte[(int)GameLayout.RenderName + 0x20];
        if (!EntityReader.Read(h, render, buf)) { return ""; }

        return info.DisplayName = EntityReader.ReadWString(h, buf, (int)GameLayout.RenderName);
    }

    private static string? RowName(IntPtr h, ulong row)
    {
        if (row < 0x10000 || row > 0x7FFFFFFFFFFF) { return null; }

        lock (Names)
        {
            if (Names.TryGetValue(row, out var cached)) { return cached; }
        }

        var first = new byte[8];
        if (!EntityReader.Read(h, row, first)) { return null; }

        // Metin de dat dosyasinin degisken bolumunde: sadece 2 bayta hizali (Waypoint, MapDevice... 8'e hizali
        // degil). Hizali isaretci isteyen LooksLikePointer burada yarisini eliyordu.
        var text = BitConverter.ToUInt64(first, 0);
        if (text < 0x10000 || text > 0x7FFFFFFFFFFF || (text & 1) != 0) { return null; }

        var raw = new byte[MaxNameLength * 2];
        if (!EntityReader.Read(h, text, raw)) { return null; }

        var sb = new StringBuilder();
        for (var i = 0; i + 1 < raw.Length; i += 2)
        {
            var c = (char)(raw[i] | (raw[i + 1] << 8));
            if (c == 0) { break; }
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') { return null; }
            sb.Append(c);
        }

        if (sb.Length == 0 || sb.Length >= MaxNameLength) { return null; }

        var name = sb.ToString();
        lock (Names) { Names[row] = name; }
        return name;
    }
}
