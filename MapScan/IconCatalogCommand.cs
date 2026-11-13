namespace Poe2Map;

/// <summary>
///     MapScan iconcatalog &lt;ExileCore.dll&gt; [cikti]: ExileApi'nin harita simgesi listesini (MapIconsIndex)
///     yerel bir katalog dosyasina yazar; her satir "indeks TAB ad". Okuma IconCatalogFile'da (derleme yuklenmiyor).
///
///     Cikti ucuncu taraf veridir: depoya girmez (data/icons/ .gitignore'da). Dagitilan overlay ayni veriyi ilk
///     acilista kendisi indiriyor (MapOverlay\IconDownload.cs); bu komut gelistirme ve elle kurulum icin.
///
///     Cikti yolu verilmezse data/icons/catalog.txt'ye yazilir.
/// </summary>
internal static class IconCatalogCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("kullanim: iconcatalog <ExileCore.dll> [cikti.txt]");
            return 2;
        }

        var output = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine("data", "icons", "catalog.txt"));

        List<(int Index, string Name)> rows;
        using (var stream = File.OpenRead(args[0])) { rows = IconCatalogFile.ReadNames(stream); }

        if (rows.Count == 0) { Console.WriteLine($"{IconCatalogFile.EnumName} bulunamadi"); return 1; }

        IconCatalogFile.Write(rows, output);
        Console.WriteLine($"{rows.Count} simge -> {output}");
        foreach (var group in rows.GroupBy(r => IconCategories.Of(r.Name)).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Count(),4}  {group.Key}");
        }

        return 0;
    }
}
