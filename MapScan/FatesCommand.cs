namespace Poe2Map;

/// <summary>
///     MapScan fates: Fate panelini (FateLabels) arar, kokten yolunu ve acik kartlarin odul yazilarini basar. Yamadan
///     sonra panel bulunamazsa ilk bakilacak yer; yazi offseti kaydiysa `MapScan uitext Fate` yeni offseti gosterir.
/// </summary>
internal static class FatesCommand
{
    /// <summary>fates [hepsi]: "hepsi" kapali paneli ve gizli simgeleri de basar (bayat yazi tanisi).</summary>
    internal static int Run(string[] args)
    {
        var all = args.Length > 0 && args[0].Equals("hepsi", StringComparison.OrdinalIgnoreCase);
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            if (UiFinder.FindLargeMap(h, null, requireVisible: false) is not { } map)
            {
                Console.WriteLine("buyuk harita ogesi bulunamadi");
                return 1;
            }

            var root = AltarLabels.RootOf(h, map.Address);
            var started = Environment.TickCount64;
            var panel = FateLabels.FindPanel(h, root);
            if (panel == 0)
            {
                Console.WriteLine($"Fate paneli bulunamadi (kok {root:X}, {Environment.TickCount64 - started} ms)");
                return 1;
            }

            var path = AltarLabels.PathOf(h, root, panel);
            Console.WriteLine($"panel {panel:X}  yol {(path is null ? "?" : string.Join('/', path))}  " +
                              $"({Environment.TickCount64 - started} ms)");

            var cards = FateLabels.Scan(h, panel, all) ?? new List<FateLabels.Card>();
            if (cards.Count == 0) { Console.WriteLine("acik kart yok (panel kapali)"); }

            foreach (var card in cards)
            {
                Console.WriteLine($"{card.Name}  (govde {card.Body:X})");
                foreach (var reward in card.Rewards) { Console.WriteLine("    - " + reward.Replace("\n", " / ")); }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
