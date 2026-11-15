namespace Poe2Map;

/// <summary>
///     MapScan uistr &lt;hexadres&gt; [hexboyut=900] [hedef hexadres]: bir ogenin icindeki butun std::wstring'leri (8 baytlik
///     adimlarla) ofsetleriyle basar. Hedef verilirse o adresi gosteren 8 baytlik alanlari da basar (ornek: bir Atlas thread
///     yuvasinin kendi ipucu ogesini hangi ofsette tuttugu; ipucu ogesi yuvanin cocuk vektorunde yok). Sadece okur.
/// </summary>
internal static class UiStrCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: uistr <hexadres> [hexboyut=900] [hedef hexadres] | uistr ham <hexadres>..."); return 2; }

        if (args[0] == "ham") { return Raw(args.Skip(1).Select(a => Convert.ToUInt64(a, 16))); }

        if (args[0] == "tablo") { return Table(Convert.ToUInt64(args[1], 16), args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0x48); }

        var address = Convert.ToUInt64(args[0], 16);
        var span = args.Length > 1 ? Convert.ToInt32(args[1], 16) : 0x900;
        var target = args.Length > 2 ? Convert.ToUInt64(args[2], 16) : 0UL;

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var buf = new byte[span + 0x20];
            if (!EntityReader.Read(h, address, buf)) { Console.WriteLine("okunamadi"); return 1; }

            for (var o = 0; o + 0x20 <= buf.Length; o += 8)
            {
                if (target != 0 && BitConverter.ToUInt64(buf, o) == target) { Console.WriteLine($"+{o:X3} -> hedef {target:X}"); }

                var text = EntityReader.ReadWString(h, buf, o);
                if (text.Length < 2 || text.Count(c => c >= ' ' && c < 0x7F) < text.Length * 0.8) { continue; }

                Console.WriteLine($"+{o:X3} \"{text.Replace("\n", "\\n").Replace("\r", "")}\"");
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>
    ///     uistr tablo &lt;satir&gt; [hexadim=48]: verilen satirdan geri ve ileri, +0 bir UTF-16 kimlik gosterdigi surece satirlari
    ///     yurur; her satirda +0 / +8 / +0x10 metinlerini basar. 14.11.2026: oyunun anahtar kelime aciklama tablosu (Id, Term,
    ///     Definition; ornek "ThreadInfoIncursion", "Blighted Strongbox").
    /// </summary>
    private static int Table(ulong row, int stride)
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var start = row;
            while (IdAt(h, start - (ulong)stride) != null) { start -= (ulong)stride; }

            var n = 0;
            for (var r = start; IdAt(h, r) is { } id; r += (ulong)stride, n++)
            {
                Console.WriteLine($"[{n}] {id} | {Text(h, Ptr(h, r + 8))} | {Text(h, Ptr(h, r + 0x10)).Replace("\n", "\\n").Replace("\r", "")}");
            }

            Console.WriteLine($"{n} satir, ilk {start:X}");
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static ulong Ptr(IntPtr h, ulong at)
    {
        var b = new byte[8];
        return Native.ReadProcessMemory(h, (IntPtr)at, b, (IntPtr)8, out _) ? BitConverter.ToUInt64(b, 0) : 0;
    }

    private static string Text(IntPtr h, ulong at, int max = 4096)
    {
        if (at == 0) { return ""; }
        var buf = new byte[max];
        if (!Native.ReadProcessMemory(h, (IntPtr)at, buf, (IntPtr)buf.Length, out _)) { return ""; }
        var e = 0;
        while (e + 2 <= buf.Length && (buf[e] | buf[e + 1]) != 0) { e += 2; }
        return System.Text.Encoding.Unicode.GetString(buf, 0, e);
    }

    private static string? IdAt(IntPtr h, ulong row)
    {
        var id = Text(h, Ptr(h, row), 160);
        return id.Length >= 2 && id.All(c => char.IsLetterOrDigit(c) || c == '_') ? id : null;
    }

    /// <summary>uistr ham: verilen adreslerden NUL'a kadar duz UTF-16 metin (dat satirlarinin isaret ettigi metinler).</summary>
    private static int Raw(IEnumerable<ulong> addresses)
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var buf = new byte[8192];
            foreach (var a in addresses)
            {
                Native.ReadProcessMemory(h, (IntPtr)a, buf, (IntPtr)buf.Length, out _);
                var e = 0;
                while (e + 2 <= buf.Length && (buf[e] | buf[e + 1]) != 0) { e += 2; }
                Console.WriteLine($"{a:X} \"{System.Text.Encoding.Unicode.GetString(buf, 0, e).Replace("\n", "\\n").Replace("\r", "")}\"");
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
