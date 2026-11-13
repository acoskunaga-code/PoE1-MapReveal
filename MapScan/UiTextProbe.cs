using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     MapScan uitext [regex]: arayuz agacinin tamamini (kokten, cocuk vektorleri +0xC0 / +0xC8) gezer ve icinde
///     regex'e uyan bir std::wstring tutan ogeleri, metnin offseti, agactaki yolu ve ekrandaki yeri ile basar.
///     Sadece okur. Amac: Eldritch altar etiketlerindeki "Player gains:" secenegini ve ekrandaki kutusunu bulmak.
///     Kok, buyuk harita ogesinden (UiFinder) ebeveyn zinciri (+0x1E0) yukari cikilarak bulunuyor.
/// </summary>
internal static class UiTextProbe
{
    private const int Span = 0x400;
    private const int OffSelf = 0xB8;
    private const int OffChildren = 0xC0;

    internal static int Run(string[] args)
    {
        var pattern = new Regex(args.Length > 0 ? args[0] : "gains", RegexOptions.IgnoreCase);
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

            var root = map.Address;
            while (UiFinder.Reread(h, root) is { ParentPtr: not 0 } e && UiFinder.Reread(h, e.ParentPtr) is not null) { root = e.ParentPtr; }

            GetClientRect(game.MainWindowHandle, out var rect);
            var (cw, ch) = (rect.Right - rect.Left, rect.Bottom - rect.Top);
            Console.WriteLine($"kok {root:X}  istemci {cw}x{ch}  aranan /{pattern}/");

            var queue = new Queue<(ulong Address, int Depth, string Path)>();
            var seen = new HashSet<ulong> { root };
            queue.Enqueue((root, 0, "kok"));
            var visited = 0;
            var buf = new byte[Span];

            while (queue.Count > 0 && visited < 400_000)
            {
                var (address, depth, path) = queue.Dequeue();
                if (!EntityReader.Read(h, address, buf) || BitConverter.ToUInt64(buf, OffSelf) != address) { continue; }

                visited++;
                for (var o = 0; o + 0x20 <= Span; o += 8)
                {
                    var text = EntityReader.ReadWString(h, buf, o);
                    if (text.Length < 3 || !pattern.IsMatch(text)) { continue; }

                    var pos = UiFinder.ElementPosition(h, address, cw, ch);
                    var el = UiFinder.Reread(h, address);
                    var scale = ch / (float)UiFinder.BaseResY;
                    var where = pos is { } p && el is { } u
                        ? $"ekran ({p.X:F0},{p.Y:F0}) {u.SizeX * scale:F0}x{u.SizeY * scale:F0} {(u.Visible ? "gorunur" : "gizli")}"
                        : "konum yok";
                    Console.WriteLine($"{address:X} +{o:X3} derinlik {depth} {where}  yol {path}");
                    Console.WriteLine($"    \"{text.Replace("\n", "\\n").Replace("\r", "")}\"");
                }

                var first = BitConverter.ToUInt64(buf, OffChildren);
                var last = BitConverter.ToUInt64(buf, OffChildren + 8);
                if (first == 0 || last <= first || last - first > 8 * 4096) { continue; }

                var kids = new byte[(int)(last - first)];
                if (!EntityReader.Read(h, first, kids)) { continue; }

                for (var i = 0; i + 8 <= kids.Length; i += 8)
                {
                    var child = BitConverter.ToUInt64(kids, i);
                    if (PlayerChain.LooksLikePointer(child) && seen.Add(child)) { queue.Enqueue((child, depth + 1, $"{path}/{i / 8}")); }
                }
            }

            Console.WriteLine($"gezilen oge: {visited}");
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
}
