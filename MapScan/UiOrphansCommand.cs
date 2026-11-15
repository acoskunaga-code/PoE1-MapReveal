namespace Poe2Map;

/// <summary>
///     MapScan uiorphans &lt;hexebeveyn&gt;... : ebeveyni (+0x1D8) verilen oge olan AMA o ogenin cocuk vektorunde bulunmayan
///     ogeleri bulur (yigini o adresi gosteren isaretciler icin tarar, isaretcinin 0x1D8 gerisi kendini +0xB8'de gosteriyorsa
///     oge sayar) ve her birinin yazilarini basar. 14.11.2026: Atlas thread yuvalarinin ipucu ogeleri (vt+0x3150B48, 0x858
///     bayt, "Select a [Thread] type" +0x358'de) boyle; yuvalarin cocuk listesinde yoklar. Sadece okur.
/// </summary>
internal static class UiOrphansCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: uiorphans <hexebeveyn> [hexebeveyn...]"); return 2; }

        var parents = args.Select(a => Convert.ToUInt64(a, 16)).ToHashSet();
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var refs = StrScanCommand.FindPointers(h, MemoryRegions.Heap(h), parents);
            var buf = new byte[0x900];
            foreach (var (parent, holders) in refs.OrderBy(x => x.Key))
            {
                Console.WriteLine($"== ebeveyn {parent:X}: {holders.Count} isaretci");
                foreach (var at in holders)
                {
                    var element = at - (ulong)UiKidsCommand.ParentOffset;
                    if (!EntityReader.Read(h, element, buf) || BitConverter.ToUInt64(buf, UiKidsCommand.SelfOffset) != element) { continue; }

                    var vt = BitConverter.ToUInt64(buf, 0);
                    var visible = UiFinder.Reread(h, element) is { Visible: true } ? "gorunur" : "gizli";
                    Console.WriteLine($"  oge {element:X} vt {vt:X} {visible}");
                    for (var o = 0; o + 0x20 <= buf.Length; o += 8)
                    {
                        var text = EntityReader.ReadWString(h, buf, o);
                        if (text.Length < 2 || text.Count(c => c >= ' ' && c < 0x7F) < text.Length * 0.8 || text == "Fontin") { continue; }

                        Console.WriteLine($"    +{o:X3} \"{text.Replace("\n", "\\n").Replace("\r", "")}\"");
                    }
                }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
