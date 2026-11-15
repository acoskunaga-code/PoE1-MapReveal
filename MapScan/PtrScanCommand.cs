namespace Poe2Map;

/// <summary>
///     MapScan ptrany &lt;hexhedef&gt;... [adim=2]: verilen adreslere isaret eden 8 baytlari butun yazilabilir bellekte arar,
///     hizasiz olanlar dahil (adim 1/2/4/8). Dat tablolarinin satirlari tek aralikli olabildigi icin (MinimapIcons 34 bayt)
///     metin -> satir baglantisi aligned aramada cikmiyor. Sadece okur.
/// </summary>
internal static class PtrScanCommand
{
    internal static int Run(string[] args)
    {
        var step = 2;
        var targets = new HashSet<ulong>();
        foreach (var arg in args)
        {
            if (arg.StartsWith("adim=", StringComparison.OrdinalIgnoreCase)) { step = int.Parse(arg[5..]); }
            else { targets.Add(Convert.ToUInt64(arg.Replace("0x", ""), 16)); }
        }

        if (targets.Count == 0) { Console.WriteLine("kullanim: ptrany <hexhedef>... [adim=2]"); return 2; }

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var regions = MemoryRegions.Heap(h);
            var found = StrScanCommand.FindPointers(h, regions, targets, step);
            foreach (var target in targets.OrderBy(t => t))
            {
                var holders = found.TryGetValue(target, out var list) ? list : new List<ulong>();
                Console.WriteLine($"{target:X}: {holders.Count} yer");
                foreach (var at in holders.Take(40)) { Console.WriteLine($"  <- {at:X}"); }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
