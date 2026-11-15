using System.Text;

namespace Poe2Map;

/// <summary>
///     MapScan dattable &lt;hexveri&gt; &lt;hexsatirboyu&gt; [sablon]: bir dat tablosunun butun satirlarini tek satirda basar.
///     hexveri = dat nesnesinin +0x28'indeki veri nesnesi (+0x00 satir basi, +0x08 satir sonu). Her satirda: metne isaret
///     eden yerler "+o:metin", baska satira isaret edenler "+o:>Id", kalan 4 baytlik sifir olmayan yerler "+o=int".
///     Ornek (15.11.2026, Fate odulleri): dat nesnesi = "Data/AuguryRewardOutcomes.dat" adina isaret eden yer - 8,
///     veri = onun +0x28'i, satir boyu 0x6D. Sadece okur.
/// </summary>
internal static class DatTableCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("kullanim: dattable <hexveri> <hexsatirboyu>"); return 2; }

        var data = Convert.ToUInt64(args[0], 16);
        var size = Convert.ToInt32(args[1], 16);

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var begin = PlayerChain.ReadPointer(h, data);
            var end = PlayerChain.ReadPointer(h, data + 8);
            var count = (long)(end - begin) / size;
            Console.WriteLine($"satirlar {begin:X}..{end:X}  {count} satir x 0x{size:X}");

            var buf = new byte[size + 8];
            for (var r = 0L; r < count; r++)
            {
                var row = begin + (ulong)(r * size);
                if (!EntityReader.Read(h, row, buf)) { continue; }

                Console.WriteLine($"[{r}] " + string.Join("  ", Fields(h, buf, size)));
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    internal static IEnumerable<string> Fields(IntPtr h, byte[] buf, int size)
    {
        var o = 0;
        while (o + 4 <= size)
        {
            if (o + 8 <= size + 8)
            {
                var value = BitConverter.ToUInt64(buf, o);
                if (Text(h, value) is { } text) { yield return $"+{o:X}:\"{text.Replace("\n", "|")}\""; o += 8; continue; }
                if (Text(h, PlayerChain.ReadPointer(h, value)) is { } id) { yield return $"+{o:X}:>{id}"; o += 8; continue; }
            }

            var i = BitConverter.ToInt32(buf, o);
            if (i != 0) { yield return $"+{o:X}={i}"; }
            o += 4;
        }
    }

    private static string? Text(IntPtr h, ulong address)
    {
        if (address < 0x10000 || address > 0x7FFF_FFFF_FFFF || (address & 1) != 0) { return null; }

        var buf = new byte[512];
        if (!EntityReader.Read(h, address, buf)) { return null; }

        var end = 0;
        while (end + 2 <= buf.Length && (buf[end] | buf[end + 1]) != 0) { end += 2; }
        if (end < 4) { return null; }

        var s = Encoding.Unicode.GetString(buf, 0, end);
        return s.All(c => c >= 0x20 && c < 0x7F || c == '\n') ? s : null;
    }
}
