namespace Poe2Map;

/// <summary>
///     MapScan playervitals [saniye]: oyuncunun Life bilesenindeki (toplam, simdiki) int ciftlerini yarim saniyede bir
///     basar; can / mana / energy shield alanlarini canli ayirmak icin (20.11.2026, otomatik flask tusu). Sadece okur.
/// </summary>
internal static class PlayerVitalsCommand
{
    private const int Window = 0x250;

    internal static int Run(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 5;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            if (LocalPlayer.Locate(h, TerrainFinder.Find(h)) is not { } live) { Console.WriteLine("oyuncu bulunamadi"); return 1; }
            if (EntityReader.ReadInfo(h, live.Entity) is not { } info || !info.Components.TryGetValue("Life", out var life))
            {
                Console.WriteLine("oyuncunun Life bileseni yok");
                return 1;
            }

            Console.WriteLine($"oyuncu {info.Path}  Life {life:X}");
            var raw = new byte[Window];
            for (var i = 0; i < seconds * 2; i++)
            {
                if (!EntityReader.Read(h, life, raw)) { Console.WriteLine("okunamadi"); return 1; }

                // Makul ciftler: 0 < toplam < 1e6, 0 <= simdiki <= toplam.
                var pairs = new List<string>();
                for (var o = 0x10; o + 8 <= Window; o += 4)
                {
                    var total = BitConverter.ToInt32(raw, o);
                    var current = BitConverter.ToInt32(raw, o + 4);
                    if (total is > 0 and < 1_000_000 && current >= 0 && current <= total) { pairs.Add($"+{o:X}={current}/{total}"); }
                }

                Console.WriteLine($"{DateTime.Now:HH:mm:ss.f}  {string.Join("  ", pairs)}");
                Thread.Sleep(500);
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
