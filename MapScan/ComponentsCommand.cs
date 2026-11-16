using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     MapScan comps &lt;regex&gt;: yolu regex'e uyan varliklarin konumunu, oyundaki adini ve her bileseninin adresini basar.
///     Sonra `datrow &lt;adres&gt; [boy] [derinlik]` ile bilesenin icine bakilir. 16.11.2026: Fate kazaninin
///     (AuguryCardObject) acilmadan once hangi kartlari tuttugunu aramak icin. Sadece okur.
/// </summary>
internal static class ComponentsCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("kullanim: comps <regex>"); return 2; }

        var filter = new Regex(args[0], RegexOptions.IgnoreCase);
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

            foreach (var e in MinimapIconProbe.Collect(h, area.Area).Where(e => filter.IsMatch(e.Info.Path)).OrderBy(e => e.Entity))
            {
                var buf = new byte[8];
                var pos = e.Info.Components.TryGetValue("Render", out var render) &&
                          EntityReader.Read(h, render + EntityReader.RenderPosition, buf)
                    ? $"({BitConverter.ToSingle(buf, 0):F0}, {BitConverter.ToSingle(buf, 4):F0})"
                    : "(?)";
                Console.WriteLine($"== {e.Info.Path}  \"{MapIconReader.DisplayName(h, e.Info)}\"  varlik {e.Entity:X}  " +
                                  $"{(e.Awake ? "uyanik" : "uyuyan")}  {pos}");
                foreach (var (name, address) in e.Info.Components.OrderBy(c => c.Key))
                {
                    Console.WriteLine($"   {name,-20} {address:X}");
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
