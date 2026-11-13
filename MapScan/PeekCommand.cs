using System.Globalization;

namespace Poe2Map;

/// <summary>
///     Canli oyunda bir adresin ham dokumu: sifir olmayan 8 baytlar, ofsetleri adrese gore, yorumlariyla
///     (iki float / iki int32 / modul ici isaretci ve RVA'si / yigin isaretcisi / ASCII metin).
///
///     Kullanim: peek <hexadres> [hexboyut = 200] [hexgeri = 0]
///     hexgeri: dokume adresin bu kadar oncesinden basla (ofsetler yine ADRESE gore, eksili).
///
///     Modul RVA'si, Ghidra'da (imaj tabani 0x140000000) ayni yere bakmak icin: adres = 0x140000000 + RVA.
/// </summary>
internal static class PeekCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("kullanim: peek <hexadres> [hexboyut = 200] [hexgeri = 0]");
            return 2;
        }

        var address = ParseHex(args[0]);
        var length = args.Length > 1 ? (int)ParseHex(args[1]) : 0x200;
        var back = args.Length > 2 ? (int)ParseHex(args[2]) : 0;

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (handle == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            ulong moduleBase = 0, moduleSize = 0;
            try
            {
                moduleBase = (ulong)game.MainModule!.BaseAddress.ToInt64();
                moduleSize = (ulong)game.MainModule.ModuleMemorySize;
            }
            catch
            {
                // modul okunamazsa RVA yazilmaz
            }

            var buf = new byte[length];
            if (!EntityReader.Read(handle, address - (ulong)back, buf))
            {
                Console.WriteLine($"0x{address - (ulong)back:X} okunamadi");
                return 1;
            }

            Console.WriteLine($"0x{address:X}  (geri {back:X}, boyut {length:X})   modul 0x{moduleBase:X}");
            for (var i = 0; i + 8 <= buf.Length; i += 8)
            {
                var v = BitConverter.ToUInt64(buf, i);
                if (v == 0) { continue; }

                Console.WriteLine($"  {Rel(i - back),7}  0x{v:X16}{Note(handle, v, moduleBase, moduleSize)}");
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static string Note(IntPtr handle, ulong v, ulong moduleBase, ulong moduleSize)
    {
        if (moduleBase != 0 && v >= moduleBase && v < moduleBase + moduleSize)
        {
            return $"   modul RVA 0x{v - moduleBase:X}";
        }

        if (v is > 0x10000UL and < 0x7FFFFFFFFFFFUL)
        {
            var text = EntityReader.ReadAscii(handle, v, 40);
            return text.Length >= 4 ? $"   isaretci -> \"{text}\"" : "   isaretci";
        }

        var lo = BitConverter.Int32BitsToSingle((int)(v & 0xFFFFFFFF));
        var hi = BitConverter.Int32BitsToSingle((int)(v >> 32));
        static bool Sane(float f) => float.IsFinite(f) && MathF.Abs(f) is >= 0.0001f and <= 100000f;

        if (Sane(lo) && Sane(hi)) { return $"   float {lo:G6}, {hi:G6}"; }
        if (Sane(lo) && (v >> 32) == 0) { return $"   float {lo:G6}"; }
        if (v < 1_000_000UL) { return $"   ({v})"; }

        var a = (int)(v & 0xFFFFFFFF);
        var b = (int)(v >> 32);
        return a is > -100000 and < 100000 && b is > -100000 and < 100000 ? $"   iki int32 {a}, {b}" : "";
    }

    private static string Rel(int v) => v < 0 ? $"-0x{-v:X}" : $"+0x{v:X}";

    private static ulong ParseHex(string s) =>
        ulong.Parse(s.Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber);
}
