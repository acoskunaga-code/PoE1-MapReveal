namespace Poe2Map;

/// <summary>
///     Alanin cikislarini bulur: AreaTransition bileseni olan varliklar.
///
///     16.09.2026'da Valley of the Titans'ta olculdu (MapScan exits):
///       - Gercek gecis kapilari (AncientSeal1/2/3Portal) AreaTransition tasiyor; kapi
///         gibi gorunen ama gecis olmayanlarda (titan heykelleri, kamera nesneleri) yok.
///         Bu yuzden ayrac yol adi degil, bilesen.
///       - Statik nesneler alanin TAMAMI icin yuklu: 4410 varligin 3981'i haritaya
///         dagilmis sus nesnesi ve cogu uyuyan listede; 2978 birim uzaktaki kontrol
///         noktasi bile listede. Kapilar da statik oldugu icin uyuyan listedeki
///         konumlari bayat degil - iki liste de okunuyor.
/// </summary>
internal static class ExitFinder
{
    internal readonly record struct Exit(uint Id, string Name, float X, float Y);

    /// <summary>Lig engeli (IsLeagueBarrier) ve kapali olup olmadigi.</summary>
    internal readonly record struct Barrier(uint Id, float X, float Y, bool Closed);

    /// <summary>
    ///     TriggerableBlockage +0x30: 01 kapali, 00 acik. 17.11.2026 canli (`MapScan blockages izle`, Hive Fortress):
    ///     kol 01->00 dondugu anda cevresindeki zemin acildi; kapali engellerin ortasi zeminde 0, acik olanlarinki 5.
    /// </summary>
    internal const ulong BlockageClosed = 0x30;

    /// <summary>
    ///     Iki listedeki cikislari ve kapilari dondurur. Kapilar yol bulma icin: zeminde
    ///     kapali gorunen ama acilan gecitler (Radar'daki gibi TriggerableBlockage bileseni
    ///     ya da yolunda "Door" gecen varliklar). Onlar olmadan yol kapali kapinin
    ///     etrafindan dolasiyor. Yol ve bilesenler cache'ten geliyor; bir alan binlerce
    ///     varlik iceriyor, her taramada hepsini yeniden okumak bosa.
    /// </summary>
    internal static List<Exit> Find(
        IntPtr h, ulong area, Dictionary<(uint, ulong), EntityReader.EntityInfo?> cache,
        List<(float X, float Y)>? doors = null, List<Barrier>? barriers = null)
    {
        var exits = new List<Exit>();
        var state = new byte[1];

        foreach (var offset in new[] { EntityReader.AwakeMap, EntityReader.SleepingMap })
        {
            if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }

            foreach (var (id, entity) in EntityReader.WalkMap(h, map, 20000))
            {
                var key = (id, entity);
                if (!cache.TryGetValue(key, out var info))
                {
                    info = EntityReader.ReadInfo(h, entity);
                    cache[key] = info;
                }

                if (info is null) { continue; }

                var isExit = info.Components.ContainsKey("AreaTransition");
                var isDoor = doors is not null && !IsLeagueBarrier(info.Path) &&
                             (info.Components.ContainsKey("TriggerableBlockage") ||
                              info.Path.Contains("Door", StringComparison.OrdinalIgnoreCase));
                var block = 0UL;
                var isBarrier = barriers is not null && IsLeagueBarrier(info.Path) &&
                                info.Components.TryGetValue("TriggerableBlockage", out block);
                if (!isExit && !isDoor && !isBarrier) { continue; }

                if (!info.Components.TryGetValue("Render", out var render)) { continue; }
                if (!EntityReader.OwnedBy(h, render, entity)) { continue; }
                if (LocalPlayer.ReadPosition(h, render) is not { } p) { continue; }

                if (isDoor) { doors!.Add((p.X, p.Y)); }
                if (isBarrier && EntityReader.Read(h, block + BlockageClosed, state) && state[0] <= 1)
                {
                    barriers!.Add(new Barrier(id, p.X, p.Y, state[0] == 1));
                }

                if (isExit) { exits.Add(new Exit(id, DisplayName(h, render, info.Path), p.X, p.Y)); }
            }
        }

        // Ayni kapi iki listede birden gorunebilir; varlik numarasiyla tekillestiriyoruz.
        return exits.GroupBy(e => e.Id).Select(g => g.First()).ToList();
    }

    /// <summary>
    ///     Kapi sayilmayan engeller: Breach kulesinin kollari (BreachTower_ArmBlockage/Core) ve haritaya dizili
    ///     BrequelBlocker duvarlari. 17.11.2026 canli (Dunes, Hive Fortress; `MapScan blockages izle`): bunlar etkinlik
    ///     boyunca acilip kapaniyor (TriggerableBlockage +0x30: 01 kapali, 00 acik) ve oyun her degisimde zemin izgarasini
    ///     KENDISI guncelliyor (kol acilinca cevresinde 456 hucre degisti). Kapi gibi zorla acilinca yol kapali kollardan ve
    ///     duvar dizilerinden geciyordu (bir haritada 306 tane); dogru durum guncel zeminden geliyor (OverlayForm.LiveTerrain).
    ///     Ama oyun kapali engeli zemine yalnizca ag balonundayken yaziyor: uzaktakiler OverlayForm.Barriers'ta hatirlaniyor.
    /// </summary>
    private static bool IsLeagueBarrier(string path) =>
        path.Contains("/Leagues/Breach/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("Brequel", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Kapinin oyundaki gorunen adi: Render bileseninin Name alani, gecis kapilarinda gidilen alanin adi.
    ///     Okunamazsa ya da metin degilse yol adinin son parcasi.
    /// </summary>
    private static string DisplayName(IntPtr h, ulong render, string path)
    {
        var buf = new byte[(int)GameLayout.RenderName + 0x20];
        if (EntityReader.Read(h, render, buf))
        {
            var name = EntityReader.ReadWString(h, buf, (int)GameLayout.RenderName).Trim();
            if (name.Length is >= 2 and <= 80 && name.All(c => c >= 0x20 && c <= 0x7E)) { return name; }
        }

        return ShortName(path);
    }

    /// <summary>"Metadata/Terrain/.../AncientSeal1Portal" -> "AncientSeal1Portal".</summary>
    private static string ShortName(string path)
    {
        var at = path.LastIndexOf('/');
        return at >= 0 && at < path.Length - 1 ? path[(at + 1)..] : path;
    }
}
