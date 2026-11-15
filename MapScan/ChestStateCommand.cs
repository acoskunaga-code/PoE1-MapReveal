using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     MapScan chests [regex=Chests]: yolu regex'e uyan varliklarin Chest, Targetable ve StateMachine bilesenlerinin ilk
///     baytlarini 4'er baytlik sutunlar halinde basar. Bir sandigi acmadan once ve actiktan sonra calistirip ciktilari
///     karsilastirmak icin: "acildi" bayragini kendimiz buluyoruz. 14.11.2026: Faridun "Djinn's Cache" sandiklarinin
///     MinimapIcon'u yok, acildiklarini baska yerden okumak gerekiyor. Sadece okur.
/// </summary>
internal static class ChestStateCommand
{
    private static readonly (string Name, int Size)[] Parts = { ("Chest", 0x80), ("Targetable", 0x60), ("StateMachine", 0x40) };

    internal static int Run(string[] args)
    {
        var filter = new Regex(args.Length > 0 ? args[0] : "Chests", RegexOptions.IgnoreCase);
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

            foreach (var e in MinimapIconProbe.Collect(h, area.Area).Where(e => filter.IsMatch(e.Info.Path)).OrderBy(e => e.Info.Path))
            {
                Console.WriteLine($"== {e.Info.Path}  \"{MapIconReader.DisplayName(h, e.Info)}\"  varlik {e.Entity:X}  {(e.Awake ? "uyanik" : "uyuyan")}");
                foreach (var (name, size) in Parts)
                {
                    if (!e.Info.Components.TryGetValue(name, out var component)) { continue; }

                    var buf = new byte[size];
                    if (!EntityReader.Read(h, component, buf)) { continue; }

                    for (var o = 0; o < size; o += 0x20)
                    {
                        var cols = Enumerable.Range(0, 8).Select(k => BitConverter.ToUInt32(buf, o + (k * 4)).ToString("X8"));
                        Console.WriteLine($"   {name,-12} +{o:X2}  {string.Join(' ', cols)}");
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
