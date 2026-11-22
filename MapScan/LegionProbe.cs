namespace Poe2Map;

/// <summary>
///     MapScan legion: alandaki Legion varliklarinin buff ve bilesen dokumu (kullanici 21.11.2026: "legion monsterlarini
///     aktif ettigimde uzerlerinde gorunen odulleri acabilirmisin"). Odul tasiyan canavari digerlerinden ayiran isaret
///     (buff Id'si, bilesen) aranıyor. Ayrica yolunda "Legion" gecen canavar olmayan varliklar. Sadece okur.
/// </summary>
internal static class LegionProbe
{
    internal static int Run(string[] args)
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

            var all = MinimapIconProbe.Collect(h, area.Area);
            var legion = all.Where(e => HiddenMonsters.IsLegion(e.Info)).ToList();
            Console.WriteLine($"Legion canavari {legion.Count}");

            var buffs = new Dictionary<string, Dictionary<string, int>>();
            var comps = new Dictionary<string, int>();
            foreach (var e in legion)
            {
                var kind = Kind(e.Info.Path);
                foreach (var c in e.Info.Components.Keys) { comps[c] = comps.GetValueOrDefault(c) + 1; }

                if (!e.Info.Components.TryGetValue("Buffs", out var b)) { continue; }

                foreach (var id in HiddenMonsters.BuffIds(h, b).Distinct())
                {
                    if (!buffs.TryGetValue(id, out var paths)) { buffs[id] = paths = new Dictionary<string, int>(); }
                    paths[kind] = paths.GetValueOrDefault(kind) + 1;
                }
            }

            Console.WriteLine("--- buff Id -> kac canavar (en cok gecen tur)");
            foreach (var (id, paths) in buffs.OrderBy(kv => kv.Value.Values.Sum()))
            {
                var top = string.Join(", ", paths.OrderByDescending(p => p.Value).Take(4).Select(p => $"{p.Key} {p.Value}"));
                Console.WriteLine($"  {paths.Values.Sum(),5}  {id}   [{top}]");
            }

            Console.WriteLine("--- bilesen -> kac canavar");
            foreach (var (c, n) in comps.OrderBy(kv => kv.Value)) { Console.WriteLine($"  {n,5}  {c}"); }

            Console.WriteLine("--- odul buff'lari (nesnenin ilk 0x80 bayti, isaretciler cozulmus)");
            foreach (var e in legion.Where(e => e.Info.Components.ContainsKey("Buffs")))
            {
                var list = HiddenMonsters.BuffList(h, e.Info.Components["Buffs"]).Where(b => b.Id.StartsWith("legion_reward", StringComparison.Ordinal)).ToList();
                if (list.Count == 0) { continue; }

                Console.WriteLine($"== {Kind(e.Info.Path)}  varlik {e.Entity:X}");
                foreach (var (buff, id) in list)
                {
                    var raw = new byte[0x80];
                    if (!EntityReader.Read(h, buff, raw)) { continue; }

                    var cells = new List<string>();
                    for (var o = 0; o < raw.Length; o += 8)
                    {
                        var q = BitConverter.ToUInt64(raw, o);
                        if (q == 0) { continue; }

                        cells.Add($"+{o:X2}={(Resolve(h, q) is { } t ? t : $"{BitConverter.ToInt32(raw, o)}/{BitConverter.ToInt32(raw, o + 4)}")}");
                    }

                    Console.WriteLine($"   {id}: {string.Join("  ", cells)}");
                }
            }

            Console.WriteLine("--- yolunda Legion gecen diger varliklar");
            foreach (var g in all.Where(e => !e.Info.IsMonster && e.Info.Path.Contains("Legion", StringComparison.OrdinalIgnoreCase))
                         .GroupBy(e => e.Info.Path).OrderByDescending(g => g.Count()))
            {
                Console.WriteLine($"  {g.Count(),4}  {g.Key}  [{string.Join(",", g.First().Info.Components.Keys)}]");
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Isaretci bir metne ya da ilk alani metin olan bir satira gidiyorsa o metin; isaretciyse "p"; degilse null.</summary>
    private static string? Resolve(IntPtr h, ulong q)
    {
        if (q is < 0x10000 or > 0x7FFFFFFFFFFF) { return null; }

        if (Text(h, q) is { } direct) { return $"\"{direct}\""; }

        var slot = new byte[8];
        if (!EntityReader.Read(h, q, slot)) { return null; }

        return Text(h, BitConverter.ToUInt64(slot, 0)) is { } inner ? $"satir[\"{inner}\"]" : $"p{q:X}";
    }

    private static string? Text(IntPtr h, ulong p)
    {
        if (p is < 0x10000 or > 0x7FFFFFFFFFFF) { return null; }

        var raw = new byte[96];
        if (!EntityReader.Read(h, p, raw)) { return null; }

        var s = System.Text.Encoding.Unicode.GetString(raw);
        var end = s.IndexOf('\0');
        if (end < 3) { return null; }

        s = s[..end];
        return s.All(c => c is >= ' ' and < (char)0x7F) ? s : null;
    }

    private static string Kind(string path)
    {
        var name = path[HiddenMonsters.LegionPrefix.Length..];
        var at = name.IndexOf('@');
        return at > 0 ? name[..at] : name;
    }
}
