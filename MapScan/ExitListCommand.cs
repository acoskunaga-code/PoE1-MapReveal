namespace Poe2Map;

/// <summary>
///     MapScan exitlist: Radar listesinin her girdisini, cikis olarak mi yoksa "cikis degil" (ugrak, sandik, NPC) olarak mi
///     siniflandirdigimizi yazar. Liste guncellendiginde ya da bir bolgede beklenen cikis cizilmediginde ilk bakilacak yer.
///     Oyun gerekmez.
/// </summary>
internal static class ExitListCommand
{
    internal static int Run(string[] args)
    {
        var only = args.Length > 0 ? args[0] : null;
        var exits = 0;
        var skipped = 0;
        var generic = 0;

        foreach (var (id, detail, display) in ExitNames.AllEntries())
        {
            if (only is not null && !id.StartsWith(only, StringComparison.OrdinalIgnoreCase)) { continue; }

            var verdict = ExitNames.Classify(id, detail, detail);
            string tag;
            if (verdict is { IsExit: true, Display: null }) { tag = "CIKIS(ad belli degil)"; exits++; generic++; }
            else if (verdict is { IsExit: true }) { tag = "CIKIS"; exits++; }
            else { tag = "atlandi"; skipped++; }

            Console.WriteLine($"{id,-10} {tag,-21} {detail,-34} {display}");
        }

        Console.WriteLine();
        Console.WriteLine($"cikis {exits} (bunlardan adi belli olmayan {generic}), atlanan {skipped}");
        return 0;
    }
}
