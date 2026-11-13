using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Harita simgesi olan varliklarin durumunu incelemek icin iki komut (sadece okur):
///
///       minimapicons-detail [simge-regex]        eslesen varliklarin adi (Render +0x148), yolu ve butun
///                                                bilesenlerinin ilk baytlari; acilmis / acilmamis sandigi yan
///                                                yana koyup farki gormek icin
///       minimapicons-watch [saniye] [simge-regex] bilesen baytlarini 300 ms'de bir okur, degisen her dort
///                                                baytlik alani zamaniyla basar: komut calisirken sandik ac,
///                                                shrine al; "kullanildi" bayragi boyle bulunur
///
///     Varsayilan regex: Strongbox|Shrine. Surekli degisen alanlar (animasyon) uc degisimden sonra susturuluyor.
/// </summary>
internal static class MinimapIconWatch
{
    private const int Window = 0x100;

    internal static int Detail(string[] args)
    {
        var filter = new Regex(args.Length > 0 ? args[0] : "Strongbox|Shrine", RegexOptions.IgnoreCase);
        return WithArea((h, area) =>
        {
            foreach (var e in Matching(h, area, filter))
            {
                Console.WriteLine($"=== {e.Icon}  \"{RenderName(h, e.Entry.Info)}\"  {(e.Entry.Awake ? "uyanik" : "uyuyan")}  {e.Entry.Info.Path}");
                foreach (var (name, address) in e.Entry.Info.Components.OrderBy(c => c.Key, StringComparer.Ordinal))
                {
                    var buf = new byte[0x60];
                    if (!EntityReader.Read(h, address, buf)) { continue; }

                    var words = Enumerable.Range(0, buf.Length / 4).Select(i => BitConverter.ToUInt32(buf, i * 4).ToString("X8"));
                    Console.WriteLine($"  {name,-22} {string.Join(' ', words)}");

                    // Bilesenin gosterdigi metinler (dat satiri, ad): ilk 0x60 bayttaki isaretciler.
                    for (var o = 0x10; o + 8 <= buf.Length; o += 8)
                    {
                        var p = BitConverter.ToUInt64(buf, o);
                        if (!MinimapIconProbe.InRange(p) || p == e.Entry.Entity) { continue; }

                        var text = MinimapIconProbe.Text(h, p);
                        if (text.Length == 0)
                        {
                            var first = PlayerChain.ReadPointer(h, p);
                            if (MinimapIconProbe.InRange(first)) { text = MinimapIconProbe.Text(h, first); }
                            if (text.Length > 0) { text = "*" + text; }
                        }

                        if (text.Length > 0) { Console.WriteLine($"  {"",-22} +{o:X2} -> {text}"); }
                    }
                }
            }

            return 0;
        });
    }

    internal static int Watch(string[] args)
    {
        var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 300;
        var filter = new Regex(args.Length > 1 ? args[1] : "Strongbox|Shrine", RegexOptions.IgnoreCase);

        return WithArea((h, area) =>
        {
            var previous = new Dictionary<(ulong Entity, string Component), byte[]>();
            var changes = new Dictionary<(ulong, string, int), int>();
            var labels = new Dictionary<ulong, string>();
            var tracked = new List<(ulong Entity, string Component, ulong Address)>();
            var end = DateTime.UtcNow.AddSeconds(seconds);
            var nextCollect = DateTime.MinValue;

            Console.WriteLine($"izleniyor {seconds} sn: {filter}");
            while (DateTime.UtcNow < end)
            {
                if (DateTime.UtcNow >= nextCollect)
                {
                    nextCollect = DateTime.UtcNow.AddSeconds(3);
                    tracked.Clear();
                    foreach (var e in Matching(h, area, filter))
                    {
                        if (!labels.ContainsKey(e.Entry.Entity))
                        {
                            labels[e.Entry.Entity] = $"{e.Icon} \"{RenderName(h, e.Entry.Info)}\" {e.Entry.Entity:X}";
                            Console.WriteLine($"{Stamp()}  yeni: {labels[e.Entry.Entity]}");
                        }

                        foreach (var (name, address) in e.Entry.Info.Components) { tracked.Add((e.Entry.Entity, name, address)); }
                    }
                }

                foreach (var (entity, component, address) in tracked)
                {
                    var now = new byte[Window];
                    if (!EntityReader.Read(h, address, now)) { continue; }

                    if (previous.TryGetValue((entity, component), out var before))
                    {
                        Compare(labels[entity], entity, component, before, now, changes);
                    }

                    previous[(entity, component)] = now;
                }

                Thread.Sleep(300);
            }

            Console.WriteLine();
            Console.WriteLine("susturulan (surekli degisen) alanlar:");
            foreach (var group in changes.Where(c => c.Value > 3).GroupBy(c => c.Key.Item2))
            {
                Console.WriteLine($"  {group.Key}: {string.Join(' ', group.Select(c => $"+{c.Key.Item3:X2}").Distinct())}");
            }

            return 0;
        });
    }

    private static void Compare(
        string label, ulong entity, string component, byte[] before, byte[] now, Dictionary<(ulong, string, int), int> changes)
    {
        for (var o = 0; o + 4 <= Window; o += 4)
        {
            var a = BitConverter.ToUInt32(before, o);
            var b = BitConverter.ToUInt32(now, o);
            if (a == b) { continue; }

            var key = (entity, component, o);
            var count = changes.GetValueOrDefault(key) + 1;
            changes[key] = count;
            if (count > 3) { continue; }

            Console.WriteLine($"{Stamp()}  {label}  {component}+{o:X2}  {a:X8} -> {b:X8}{(count == 3 ? "  (bundan sonra susuyor)" : "")}");
        }
    }

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss.f");

    private static IEnumerable<(MinimapIconProbe.Entry Entry, string Icon)> Matching(IntPtr h, ulong area, Regex filter)
    {
        foreach (var e in MinimapIconProbe.Collect(h, area))
        {
            if (!e.Info.Components.TryGetValue(MapIconReader.ComponentName, out var component)) { continue; }

            var row = PlayerChain.ReadPointer(h, component + MapIconReader.RowPointer);
            var icon = MinimapIconProbe.InRange(row) ? MinimapIconProbe.RowId(h, row) : "?";
            if (filter.IsMatch(icon) || filter.IsMatch(e.Info.Path)) { yield return (e, icon); }
        }
    }

    /// <summary>Render bileseninin gorunen adi (+0x148 std::wstring), kapilarda gidilen alanin adi.</summary>
    internal static string RenderName(IntPtr h, EntityReader.EntityInfo info) => MapIconReader.DisplayName(h, info);

    private static int WithArea(Func<IntPtr, ulong, int> body)
    {
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

            if (area.Count == 0) { Console.WriteLine("Canli alanda varlik yok."); return 1; }

            return body(h, area.Area);
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }
}
