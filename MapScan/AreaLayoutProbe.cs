namespace Poe2Map;

/// <summary>
///     Alan nesnesinin (AreaInstance) yerlesimini ogrenir; sabit offset varsaymaz.
///
///     Zemin yapisinin +0x08'i sahibine geri isaret ediyor (PoE1'de 27.10.2026'da gorduk). Oradan
///     alan nesnesinin icini tarayip yapisal sinavla ariyoruz:
///
///         oyuncu   : yolu Metadata/Characters/ ile baslayan bir varlik isaretcisi
///         listeler : bas dugumu kirmizi-siyah agac gibi gezilebilen, dugumlerinin cogu gecerli
///                    varlik olan std::map basligi (uyanik / uyuyan)
///         konum    : oyuncunun Render bileseninde, cifti yurunebilir bir hucreye dusen float cifti
///         baglanti : oyuncu varliginin icinde alan nesnesinin adresini tutan alan
///
///     PoE2'deki degerler (zemin +0x8D0, oyuncu +0x5D0, listeler +0x6F0 / +0x700, Render konumu +0x138,
///     varlik -> alan +0x78) sadece karsilastirma icin yazdiriliyor. Varlik okuyucusu (EntityReader) da
///     PoE2 yerlesimini varsayiyor; PoE1'de yol ve bilesen adlari dogru cozuldu, yani o kisim ayni.
/// </summary>
internal static class AreaLayoutProbe
{
    private const int Window = 0x1800;

    internal static int Run()
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var terrains = TerrainFinder.Find(h);
            if (terrains.Count == 0) { Console.WriteLine("zemin yapisi bulunamadi"); return 1; }

            foreach (var t in terrains.OrderByDescending(t => t.DataLength)) { Probe(h, t); }
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static void Probe(IntPtr h, TerrainFinder.Terrain t)
    {
        var b = t.StructAddress;
        Console.WriteLine($"== zemin yapisi 0x{b:X}  ({t.TilesX}x{t.TilesY}) ==");

        // Yapinin +0x08'i sahibine (alan nesnesi) geri isaret ediyor mu?
        var back = PlayerChain.ReadPointer(h, b + 0x08);
        if (!PlayerChain.LooksLikePointer(back) || back >= b || b - back > 0x4000)
        {
            Console.WriteLine($"geri isaretci +0x08 = 0x{back:X}: alan nesnesine benzemiyor");
            return;
        }

        var area = back;
        Console.WriteLine($"alan nesnesi adayi 0x{area:X}   zemin ofseti +0x{b - area:X}   (PoE2: +0x8D0)");

        var buf = new byte[Window];
        if (!EntityReader.Read(h, area, buf)) { Console.WriteLine("alan nesnesi okunamadi"); return; }

        FindPlayer(h, buf, t, area);
        FindMaps(h, area);
        Console.WriteLine("\nkarsilastirma, PoE2: oyuncu +0x5D0, uyanik +0x6F0, uyuyan +0x700");
    }

    private static void FindPlayer(IntPtr h, byte[] buf, TerrainFinder.Terrain t, ulong area)
    {
        Console.WriteLine("\noyuncu adaylari (yolu Metadata/Characters/ ile baslayan varlik isaretcisi):");

        var printed = new HashSet<ulong>();
        int pointers = 0, entities = 0, players = 0;

        for (var k = 0; k + 8 <= buf.Length; k += 8)
        {
            var v = BitConverter.ToUInt64(buf, k);
            if (!PlayerChain.LooksLikePointer(v)) { continue; }

            pointers++;
            if (EntityReader.ReadInfo(h, v) is not { } info) { continue; }

            entities++;
            if (!info.Path.StartsWith("Metadata/Characters/", StringComparison.Ordinal)) { continue; }

            players++;
            if (!printed.Add(v))
            {
                Console.WriteLine($"  +0x{k:X}  ayni varlik 0x{v:X}");
                continue;
            }

            PrintEntity(h, k, v, info, t, area);
        }

        Console.WriteLine($"  ({pointers} isaretci, {entities} tanesi gecerli varlik yoluna cozuldu)");
        if (players == 0) { Console.WriteLine("  oyuncu bulunamadi"); }
    }

    private static void PrintEntity(
        IntPtr h, int k, ulong entity, EntityReader.EntityInfo info, TerrainFinder.Terrain t, ulong area)
    {
        Console.WriteLine($"  +0x{k:X}  varlik 0x{entity:X}  {info.Path}");
        Console.WriteLine($"     bilesenler ({info.Components.Count}): " + string.Join(", ", info.Components.Keys.Take(16)));

        ScanAreaLink(h, entity, area);

        if (!info.Components.TryGetValue("Render", out var render)) { return; }

        Console.WriteLine($"     Render 0x{render:X}  sahibi={EntityReader.OwnedBy(h, render, entity)}");
        ScanRenderPosition(h, render, t);
    }

    /// <summary>Varligin icinde alan nesnesinin adresini tutan alanlar (PoE2'de +0x78).</summary>
    private static void ScanAreaLink(IntPtr h, ulong entity, ulong area)
    {
        var data = new byte[0x400];
        if (!EntityReader.Read(h, entity, data)) { return; }

        var offsets = new List<string>();
        for (var k = 0; k + 8 <= data.Length; k += 8)
        {
            if (BitConverter.ToUInt64(data, k) == area) { offsets.Add($"+0x{k:X}"); }
        }

        Console.WriteLine("     varlik -> alan baglantisi: " + (offsets.Count > 0 ? string.Join(", ", offsets) : "yok") +
                          "   (PoE2: +0x78)");
    }

    /// <summary>
    ///     Render bileseninde ciftti yurunebilir bir hucreye dusen float ciftleri. Oyuncu yurunebilir
    ///     bir yerde durdugu icin gercek konum bunlardan biri; rastgele float ciftleri bu sinavi gecmez.
    /// </summary>
    private static void ScanRenderPosition(IntPtr h, ulong render, TerrainFinder.Terrain t)
    {
        var data = new byte[0x300];
        if (!EntityReader.Read(h, render, data)) { Console.WriteLine("     Render bileseni okunamadi"); return; }

        var maxX = t.GridX * TerrainFinder.WorldPerCell;
        var maxY = t.GridY * TerrainFinder.WorldPerCell;
        Console.WriteLine($"     konum adaylari (float cifti yurunebilir hucrede; alan 0..{maxX:F0} x 0..{maxY:F0}, PoE2: +0x138):");

        var found = 0;
        for (var k = 8; k + 12 <= data.Length; k += 4)
        {
            var x = BitConverter.ToSingle(data, k);
            var y = BitConverter.ToSingle(data, k + 4);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0 || x >= maxX || y >= maxY) { continue; }
            if (!PlayerFinder.Walkable(h, t, x, y)) { continue; }

            found++;
            Console.WriteLine($"       +0x{k:X}  ({x:F1}, {y:F1}, {BitConverter.ToSingle(data, k + 8):F1})");
        }

        if (found == 0) { Console.WriteLine("       yok"); }
    }

    private static void FindMaps(IntPtr h, ulong area)
    {
        Console.WriteLine("\nvarlik listesi adaylari (std::map: bas dugum + boyut):");

        var found = 0;
        for (var k = 0; k + 16 <= Window; k += 8)
        {
            if (EntityReader.ReadMapHeader(h, area + (ulong)k) is not { Size: > 0 } map) { continue; }

            var nodes = EntityReader.WalkMap(h, map, 3000);
            if (nodes.Count == 0) { continue; }

            var sample = nodes.Take(40)
                .Select(n => EntityReader.ReadInfo(h, n.Entity))
                .Where(i => i is not null)
                .Select(i => i!)
                .ToList();
            if (sample.Count == 0) { continue; }

            found++;
            var hasPlayer = sample.Any(s => s.Path.StartsWith("Metadata/Characters/", StringComparison.Ordinal));
            var paths = string.Join(" | ", sample.Select(s => s.Path).Distinct().Take(3));
            Console.WriteLine($"  +0x{k:X}  boyut {map.Size}  gezilen {nodes.Count}  " +
                              $"gecerli {sample.Count}/{Math.Min(40, nodes.Count)}  " +
                              (hasPlayer ? "[oyuncu burada]  " : "") + paths);
        }

        if (found == 0) { Console.WriteLine("  liste bulunamadi"); }
    }
}
