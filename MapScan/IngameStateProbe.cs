namespace Poe2Map;

/// <summary>
///     Oyunun UI agacina ALAN NESNESINDEN ulasir; sabit adres ya da ucuncu taraf offseti varsaymaz.
///
///     Ipucu (GameOffsets.dll, IngameDataOffsets alan SIRASI, offsetsiz): alan nesnesi oyun durumu
///     (IngameState) nesnesine bir isaretci tasiyor. Burada sadece yapisal sinav kullaniliyor: alan
///     nesnesinin penceresindeki bir isaretcinin gosterdigi nesne
///         (a) alan nesnesinin adresini bir alanda tasiyor (geri baglanti) ve
///         (b) +0x08'i kendini gosteren (UI ogesi) alanlar tasiyorsa
///     oyun durumu adayidir. Oyuncu varligi da geri baglanti tasir ama UI ogesi tasimaz; ayrim (b)'den.
///
///     Her UI ogesi icin cocuk vektoru de gozlemden cikarilir: (bas, son) cifti, aradaki isaretci sayisi
///     makul ve ilk/son cocuk da UI ogesi.
///
///         ingame
/// </summary>
internal static class IngameStateProbe
{
    private const int AreaWindow = 0x1800;
    private const int StateWindow = 0x800;

    private static ulong moduleBase;
    private static ulong moduleSize;

    internal static int Run()
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        try
        {
            moduleBase = (ulong)game.MainModule!.BaseAddress.ToInt64();
            moduleSize = (ulong)game.MainModule.ModuleMemorySize;
        }
        catch
        {
            // modul okunamazsa vtable isareti "yok" yazar
        }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var terrains = TerrainFinder.Find(h);
            if (terrains.Count == 0) { Console.WriteLine("zemin yapisi bulunamadi"); return 1; }

            var area = PlayerChain.ReadPointer(h, terrains.OrderByDescending(t => t.DataLength).First().StructAddress + 0x08);
            var areaBuf = new byte[AreaWindow];
            if (!PlayerChain.LooksLikePointer(area) || !EntityReader.Read(h, area, areaBuf))
            {
                Console.WriteLine("alan nesnesi okunamadi");
                return 1;
            }

            Console.WriteLine($"alan nesnesi 0x{area:X}\n");
            for (var o = 0; o + 8 <= AreaWindow; o += 8)
            {
                var p = BitConverter.ToUInt64(areaBuf, o);
                if (!PlayerChain.LooksLikePointer(p) || p == area) { continue; }

                Candidate(h, area, o, p);
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static void Candidate(IntPtr h, ulong area, int areaSlot, ulong state)
    {
        var buf = new byte[StateWindow];
        if (!EntityReader.Read(h, state, buf)) { return; }

        var backs = new List<int>();
        var elements = new List<(int Slot, ulong Address)>();
        for (var s = 0; s + 8 <= StateWindow; s += 8)
        {
            var q = BitConverter.ToUInt64(buf, s);
            if (q == area) { backs.Add(s); }
            else if (IsElement(h, q) && q != state) { elements.Add((s, q)); }
        }

        if (backs.Count == 0) { return; }

        var vtable = BitConverter.ToUInt64(buf, 0);
        var inModule = vtable >= moduleBase && vtable < moduleBase + moduleSize;
        var pointers = 0;
        for (var s = 0; s + 8 <= StateWindow; s += 8)
        {
            if (PlayerChain.LooksLikePointer(BitConverter.ToUInt64(buf, s))) { pointers++; }
        }

        Console.WriteLine($"alan+0x{areaSlot:X} -> 0x{state:X}   geri baglanti: " +
                          string.Join(", ", backs.Select(b => "+0x" + b.ToString("X"))) +
                          $"   vtable {(inModule ? "modulde" : "yok")}   isaretci {pointers}   UI ogesi alani {elements.Count}");
        foreach (var (slot, address) in elements.Take(40))
        {
            var kids = ChildVector(h, address);
            Console.WriteLine($"    +0x{slot,-5:X} 0x{address:X}   " +
                              (kids.Count > 0 ? $"cocuk vektoru +0x{kids.Offset:X}: {kids.Count} cocuk" : "cocuk vektoru yok"));
        }
    }

    /// <summary>UI ogesi sinavi: +0x08 kendi adresini gosteriyor (UiFinder / UiSnapshot ile ayni).</summary>
    private static bool IsElement(IntPtr h, ulong address) =>
        PlayerChain.LooksLikePointer(address) && PlayerChain.ReadPointer(h, address + 8) == address;

    /// <summary>
    ///     Ogenin ilk 0xC0 baytinda (bas, son) vektoru arar: 8'in kati bir fark, makul cocuk sayisi, ilk ve
    ///     son cocuk UI ogesi. Bulursa (ofset, cocuk sayisi); yoksa (-1, 0).
    /// </summary>
    internal static (int Offset, int Count) ChildVector(IntPtr h, ulong element)
    {
        var raw = new byte[0xC8];
        if (!EntityReader.Read(h, element, raw)) { return (-1, 0); }

        for (var o = 0x10; o + 16 <= raw.Length; o += 8)
        {
            var first = BitConverter.ToUInt64(raw, o);
            var last = BitConverter.ToUInt64(raw, o + 8);
            if (!PlayerChain.LooksLikePointer(first) || last <= first || ((last - first) & 7) != 0) { continue; }

            var count = (last - first) / 8;
            if (count is < 1 or > 5000) { continue; }

            if (IsElement(h, PlayerChain.ReadPointer(h, first)) && IsElement(h, PlayerChain.ReadPointer(h, last - 8)))
            {
                return (o, (int)count);
            }
        }

        return (-1, 0);
    }
}
