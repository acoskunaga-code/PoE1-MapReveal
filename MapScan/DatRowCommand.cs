using System.Text;

namespace Poe2Map;

/// <summary>
///     MapScan datrow &lt;hexsatir&gt; [hexboy=58] [derinlik=1] [adim=4]: bellekteki bir dat satirini 4 bayt adimlarla cozer. Her yerde
///     8 bayti isaretci sayip baktigi seyi yazar: UTF-16 metin, ya da +0'i metne isaret eden baska bir satir (yabanci anahtar)
///     - derinlik > 0 ise o satir da ayni sekilde cozulur. Isaretci olmayan yerlerde int32. Dat satirlari tek aralikli
///     olabildigi icin (MinimapIcons 34 bayt) isaretciler hizasiz okunuyor. Sadece okur.
/// </summary>
internal static class DatRowCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: datrow <hexsatir> [hexboy=58] [derinlik=1] [adim=4]"); return 2; }

        var row = Convert.ToUInt64(args[0], 16);
        var length = args.Length > 1 ? Convert.ToInt32(args[1], 16) : 0x58;
        var depth = args.Length > 2 ? int.Parse(args[2]) : 1;
        var step = args.Length > 3 ? int.Parse(args[3]) : 4;

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            Print(h, row, length, depth, "", step);
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    internal static void Print(IntPtr h, ulong row, int length, int depth, string indent, int step = 4)
    {
        var buf = new byte[length + 8];
        if (!EntityReader.Read(h, row, buf)) { Console.WriteLine($"{indent}{row:X}: okunamadi"); return; }

        for (var o = 0; o + 4 <= length; o += step)
        {
            var value = BitConverter.ToUInt64(buf, o);
            if (Text(h, value) is { } text)
            {
                Console.WriteLine($"{indent}+{o:X2} metin \"{text}\"");
                o += 8 - step;
                continue;
            }

            if (Text(h, PlayerChain.ReadPointer(h, value)) is { } id)
            {
                Console.WriteLine($"{indent}+{o:X2} satir {value:X} (Id \"{id}\")");
                if (depth > 0) { Print(h, value, 0x40, depth - 1, indent + "      "); }
                o += 8 - step;
                continue;
            }

            var i = BitConverter.ToInt32(buf, o);
            if (i != 0 && o % 4 == 0) { Console.WriteLine($"{indent}+{o:X2} int {i} (0x{i:X})"); }
        }
    }

    /// <summary>Adreste en az 2 harfli, yazdirilabilir bir UTF-16 metin varsa o; yoksa null.</summary>
    private static string? Text(IntPtr h, ulong address)
    {
        if (address < 0x10000 || address > 0x7FFF_FFFF_FFFF || (address & 1) != 0) { return null; }

        var buf = new byte[256];
        if (!EntityReader.Read(h, address, buf)) { return null; }

        var end = 0;
        while (end + 2 <= buf.Length && (buf[end] | buf[end + 1]) != 0) { end += 2; }
        if (end < 4) { return null; }

        var s = Encoding.Unicode.GetString(buf, 0, end);
        return s.All(c => c >= 0x20 && c < 0x7F || c == '\n') ? (end == buf.Length ? s + "..." : s) : null;
    }
}
