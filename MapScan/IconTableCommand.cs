using System.Text;

namespace Poe2Map;

/// <summary>
///     MapScan icontable [catalog.txt]: oyunun kendi MinimapIcons.dat tablosunu bellekten bastan sona okur (satir sirasi
///     -> Id) ve ExileCore'dan cikarilan katalogla (indeks -> ad) karsilastirir. Soru: ikon resmindeki (Icons.png) hucre
///     sirasi oyunun satir sirasiyla ayni mi; ayniysa adlar DLL indirmeden oyundan alinabilir.
///
///     Tablo yeri: canli alandaki MinimapIcon bilesenlerinin +0x20 satir isaretcileri (satirlar 34 bayt aralikli). Tablonun
///     basi ve sonu iki yoldan: dat nesnesinde (bilesen +0x28) satirlari kapsayan bir isaretci cifti, ve satirdan geriye /
///     ileriye Id metni gecerli oldugu surece yurumek. Ikisinin ayni sonucu vermesi de bir sinav.
/// </summary>
internal static class IconTableCommand
{
    private const int RowSize = 34;
    private const string ComponentName = "MinimapIcon";

    internal static int Run(string[] args)
    {
        var catalogFile = args.Length > 0 ? args[0] : AppPaths.File(Path.Combine("icons", "catalog.txt"));
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var area = TerrainFinder.Find(h, _ => { })
                .Select(t => t.StructAddress - GameLayout.TerrainInArea)
                .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
                .OrderByDescending(x => x.Count)
                .FirstOrDefault();
            if (area.Count == 0) { Console.WriteLine("Canli alanda varlik yok."); return 1; }

            var shared = IconTable.Read(h, area.Area);
            Console.WriteLine($"IconTable.Read (overlay'in yolu): {(shared is null ? "null" : shared.Count + " satir")}");

            var components = MinimapIconProbe.Collect(h, area.Area)
                .Where(e => e.Info.Components.ContainsKey(ComponentName))
                .Select(e => e.Info.Components[ComponentName])
                .ToList();
            var rows = components.Select(c => PlayerChain.ReadPointer(h, c + 0x20))
                .Where(MinimapIconProbe.InRange).Distinct().OrderBy(r => r).ToList();
            if (rows.Count == 0) { Console.WriteLine("MinimapIcon satiri bulunamadi."); return 1; }

            Console.WriteLine($"simge bileseni {components.Count}, farkli satir {rows.Count}: {rows[0]:X} .. {rows[^1]:X}");
            var bad = rows.Where(r => (r - rows[0]) % RowSize != 0).ToList();
            Console.WriteLine(bad.Count == 0 ? "satirlar 34 bayt adimli: evet" : $"34 bayt adimina uymayan satir: {bad.Count}");

            var fromDat = FromDatObject(h, PlayerChain.ReadPointer(h, components[0] + 0x28), rows[0], rows[^1]);
            var fromWalk = FromWalk(h, rows[0], rows[^1]);
            Console.WriteLine($"dat nesnesinden: {(fromDat is { } d ? $"{d.Begin:X} .. {d.End:X}, {(d.End - d.Begin) / RowSize} satir" : "bulunamadi")}");
            Console.WriteLine($"yuruyerek:      {fromWalk.Begin:X} .. {fromWalk.End:X}, {(fromWalk.End - fromWalk.Begin) / RowSize} satir");

            var (begin, end) = fromDat ?? fromWalk;
            var ids = new List<string>();
            for (var row = begin; row < end; row += RowSize) { ids.Add(Id(h, row)); }

            Compare(ids, catalogFile);
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Dat nesnesinin ilk 0x100 baytinda satirlari kapsayan, 34'e bolunen bir [bas, son) isaretci cifti.</summary>
    private static (ulong Begin, ulong End)? FromDatObject(IntPtr h, ulong dat, ulong first, ulong last)
    {
        var buf = new byte[0x100];
        if (!EntityReader.Read(h, dat, buf)) { return null; }

        for (var o = 0; o + 16 <= buf.Length; o += 8)
        {
            var b = BitConverter.ToUInt64(buf, o);
            var e = BitConverter.ToUInt64(buf, o + 8);
            if (b == 0 || b > first || e <= last || (first - b) % RowSize != 0 || (e - b) % RowSize != 0) { continue; }
            if ((e - b) / RowSize > 20_000) { continue; }

            Console.WriteLine($"  dat nesnesi {dat:X} +0x{o:X2}/+0x{o + 8:X2}: satir dizisi");
            return (b, e);
        }

        return null;
    }

    /// <summary>Bilinen satirlardan geriye ve ileriye, Id metni okunabildigi surece.</summary>
    private static (ulong Begin, ulong End) FromWalk(IntPtr h, ulong first, ulong last)
    {
        var begin = first;
        while (Id(h, begin - RowSize).Length > 0) { begin -= RowSize; }

        var end = last + RowSize;
        while (Id(h, end).Length > 0) { end += RowSize; }

        return (begin, end);
    }

    /// <summary>Satirin Id'si (satir +0 -> UTF-16, yalniz harf/rakam/_); okunamazsa "".</summary>
    private static string Id(IntPtr h, ulong row)
    {
        var p = new byte[8];
        if (!EntityReader.Read(h, row, p)) { return ""; }

        var text = new byte[160];
        if (!EntityReader.Read(h, BitConverter.ToUInt64(p, 0), text)) { return ""; }

        var sb = new StringBuilder();
        for (var i = 0; i + 1 < text.Length; i += 2)
        {
            var c = (char)(text[i] | (text[i + 1] << 8));
            if (c == 0) { break; }
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') { return ""; }
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Oyun satiri i (0'dan) ile katalog indeksi i+1 ayni ad mi.</summary>
    private static void Compare(List<string> ids, string catalogFile)
    {
        if (!File.Exists(catalogFile)) { Console.WriteLine($"katalog yok: {catalogFile}"); return; }

        var catalog = new Dictionary<int, string>();
        foreach (var line in File.ReadLines(catalogFile))
        {
            var tab = line.IndexOf('\t');
            if (line.StartsWith('#') || tab <= 0 || !int.TryParse(line[..tab], out var index)) { continue; }
            catalog[index] = line[(tab + 1)..].Trim();
        }

        var same = 0;
        var diff = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            if (!catalog.TryGetValue(i + 1, out var name)) { continue; }
            if (string.Equals(name, ids[i], StringComparison.Ordinal)) { same++; }
            else { diff.Add($"  satir {i} (indeks {i + 1}): oyun {ids[i]}  katalog {name}"); }
        }

        Console.WriteLine($"oyun tablosu {ids.Count} satir, katalog {catalog.Count} ad; ayni {same}, farkli {diff.Count}");
        foreach (var line in diff.Take(40)) { Console.WriteLine(line); }

        if (ids.Count > catalog.Count)
        {
            Console.WriteLine($"oyunda katalogdan fazla {ids.Count - catalog.Count} satir (resimde hucresi yok): " +
                              string.Join(", ", ids.Skip(catalog.Count).Take(20)));
        }
    }
}
