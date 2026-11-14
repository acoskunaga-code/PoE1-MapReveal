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

            // Ekrandaki yerler (overlay'in kutu yerlesimi bunlarla): istemci boyutu oyun penceresinden.
            GetClientRect(game.MainWindowHandle, out var rc);
            var (cw, ch) = (rc.Right - rc.Left, rc.Bottom - rc.Top);
            foreach (var card in cards)
            {
                Console.WriteLine($"{card.Name}  (govde {card.Body:X})");
                foreach (var reward in card.Rewards) { Console.WriteLine("    - " + reward.Replace("\n", " / ")); }
                foreach (var part in card.Parts)
                {
                    var at = UiFinder.ElementPosition(h, part, cw, ch);
                    var el = UiFinder.Reread(h, part);
                    var size = el is { } u ? UiFinder.ScaleValue(u.ScaleIndex, u.LocalScale, cw, ch, 0) : (W: 0f, H: 0f);
                    Console.WriteLine($"    oge {part:X}: " + (at is { } p && el is { } e
                        ? $"({p.X:F0},{p.Y:F0}) {e.SizeX * size.W:F0}x{e.SizeY * size.H:F0} {(e.Visible ? "gorunur" : "gizli")}"
                        : "yer yok"));
                }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
}
