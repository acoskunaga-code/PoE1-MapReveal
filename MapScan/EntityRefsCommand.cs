using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     MapScan entityrefs &lt;yol-regex&gt; [en-cok]: eslesen varliklarin bilesenlerinde baska bir VARLIGA giden
///     isaretcileri arar (iki kademeye kadar) ve o varligin yolunu basar. Sadece okur. Amac: ayni yolu paylasan
///     varliklarin (ornek: AzmeriResourceBase wisp'leri) turunu ayiran bagli nesneyi bulmak; animasyon nesnesi
///     ya da efekt varligi gibi. Bulunan yol bir varliga gore degisiyorsa tur oradadir.
/// </summary>
internal static class EntityRefsCommand
{
    private const int Window = 0x300;

    internal static int Run(string[] args)
    {
        var filter = new Regex(args.Length > 0 ? args[0] : "AzmeriResource", RegexOptions.IgnoreCase);
        var max = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 6;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var area = TerrainFinder.Find(h, _ => { })
                .Select(t => t.StructAddress - GameLayout.TerrainInArea)
                .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
                .OrderByDescending(x => x.Count)
                .FirstOrDefault();

            var picked = MinimapIconProbe.Collect(h, area.Area)
                .Where(e => e.Awake && filter.IsMatch(e.Info.Path))
                .GroupBy(e => e.Info.Path)
                .SelectMany(g => g.Take(max));

            foreach (var e in picked)
            {
                Console.WriteLine($"=== {e.Info.Path}  {e.Entity:X}");
                foreach (var (name, address) in e.Info.Components)
                {
                    var buf = new byte[Window];
                    if (!EntityReader.Read(h, address, buf)) { continue; }

                    for (var o = 0x10; o + 8 <= Window; o += 8)
                    {
                        var p = BitConverter.ToUInt64(buf, o);
                        if (!PlayerChain.LooksLikePointer(p) || p == e.Entity || p == address) { continue; }

                        if (Describe(h, p) is { } direct) { Console.WriteLine($"  {name}+{o:X} -> {direct}"); continue; }

                        var inner = new byte[0x80];
                        if (!EntityReader.Read(h, p, inner)) { continue; }

                        for (var i = 0; i + 8 <= inner.Length; i += 8)
                        {
                            var q = BitConverter.ToUInt64(inner, i);
                            if (q == e.Entity || q == p || !PlayerChain.LooksLikePointer(q)) { continue; }
                            if (Describe(h, q) is { } nested) { Console.WriteLine($"  {name}+{o:X} ->+{i:X} -> {nested}"); }
                        }
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

    /// <summary>Adres bir varlik mi (yolu Metadata/ ile baslayan); oyleyse yolu ve gorunen adi.</summary>
    private static string? Describe(IntPtr h, ulong address)
    {
        if (EntityReader.ReadInfo(h, address) is not { } info) { return null; }

        var name = MapIconReader.DisplayName(h, info);
        return $"varlik {info.Path}{(name.Length > 0 ? $" \"{name}\"" : "")}";
    }
}
