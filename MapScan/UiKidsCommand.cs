namespace Poe2Map;

/// <summary>
///     UI agacinda gezinme: bir ogenin cocuklarini listeler, istenirse alt katmanlara iner.
///
///     Yerlesim sabitleri (PoE1, 27.10.2026 surumu) uiclass ile CANLI bulundu, kimseden alinmadi:
///         kendini gosteren alan +0xB8, cocuk vektoru +0xC0 / +0xC8, ebeveyn +0x1E0
///     (oy sayilari: ebeveyn +0x1E0 icin 57944, ikinci aday 129). Surum degisince uiclass yeniden calistirilir.
///
///         uikids <hexadres> [derinlik=1] [en cok cocuk=60]
/// </summary>
internal static class UiKidsCommand
{
    internal const int SelfOffset = 0xB8;
    internal const int ChildVectorOffset = 0xC0;
    internal const int ParentOffset = 0x1D8;   // 27.10: 0x1E0; 13.11.2026 `MapScan uiclass canli:B8` 2848 oy
    private const int Window = 0x480;

    private static ulong moduleBase;

    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: uikids <hexadres> [derinlik=1] [en cok cocuk=60]"); return 2; }

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            moduleBase = (ulong)game.MainModule!.BaseAddress.ToInt64();
            var address = Convert.ToUInt64(args[0], 16);
            var depth = args.Length > 1 ? int.Parse(args[1]) : 1;
            var max = args.Length > 2 ? int.Parse(args[2]) : 60;
            Print(h, address, depth, max, 0);
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    internal static ulong[]? Children(IntPtr h, byte[] raw)
    {
        var first = BitConverter.ToUInt64(raw, ChildVectorOffset);
        var last = BitConverter.ToUInt64(raw, ChildVectorOffset + 8);
        if (!PlayerChain.LooksLikePointer(first) || last < first || ((last - first) & 7) != 0 || (last - first) / 8 > 5000)
        {
            return null;
        }

        var bytes = new byte[last - first];
        if (bytes.Length == 0) { return Array.Empty<ulong>(); }
        if (!EntityReader.Read(h, first, bytes)) { return null; }

        var items = new ulong[bytes.Length / 8];
        Buffer.BlockCopy(bytes, 0, items, 0, bytes.Length);
        return items;
    }

    private static void Print(IntPtr h, ulong address, int depth, int max, int level)
    {
        var raw = new byte[Window];
        var pad = new string(' ', level * 3);
        if (!EntityReader.Read(h, address, raw)) { Console.WriteLine($"{pad}0x{address:X}  okunamadi"); return; }

        var kids = Children(h, raw);
        Console.WriteLine($"{pad}{Describe(address, raw, kids?.Length)}");
        if (kids == null || level >= depth) { return; }

        foreach (var kid in kids.Take(max)) { Print(h, kid, depth, max, level + 1); }
        if (kids.Length > max) { Console.WriteLine($"{pad}   ... {kids.Length - max} cocuk daha"); }
    }

    internal static string Describe(ulong address, byte[] raw, int? kids)
    {
        var self = BitConverter.ToUInt64(raw, SelfOffset) == address ? "" : " (KENDI ISARETCISI TUTMUYOR)";
        return $"0x{address:X}  vt+0x{BitConverter.ToUInt64(raw, 0) - moduleBase:X}  boyut 0x{BitConverter.ToUInt64(raw, 8):X}  " +
               $"cocuk {(kids.HasValue ? kids.Value.ToString() : "?")}  ebeveyn 0x{BitConverter.ToUInt64(raw, ParentOffset):X}  " +
               $"+0x150=({Fl(raw, 0x150)}, {Fl(raw, 0x154)})  +0x268=({Fl(raw, 0x268)}, {Fl(raw, 0x26C)}){self}";
    }

    private static string Fl(byte[] raw, int offset) => BitConverter.ToSingle(raw, offset).ToString("G5");
}
