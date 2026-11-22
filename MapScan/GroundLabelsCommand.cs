namespace Poe2Map;

/// <summary>
///     MapScan groundlabels [yol = 1/6/0] [adet = 20]: yerdeki esya yazilarinin kabi (18.11.2026 `MapScan uitext
///     "^Heavy Belt$"`: kok/1/6/0/&lt;n&gt;, her cocuk bir esya yazisi) ve her yazinin hangi varliga bagli oldugu. Her yazi
///     ogesinin ilk 0x800 baytinda (ve bir kademe icinde) WorldItem varligina ya da esya varligina giden isaretciyi arar.
///     Amac: istenen unique'in oyundaki yazisini cercevelemek (altar kutulari gibi). Sadece okur.
/// </summary>
internal static class GroundLabelsCommand
{
    private const int Span = 0x800;

    internal static int Run(string[] args)
    {
        var path = (args.Length > 0 ? args[0] : "1/6/0").Split('/').Select(int.Parse).ToArray();
        var count = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 20;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            if (UiFinder.FindLargeMap(h, null, requireVisible: false) is not { } map) { Console.WriteLine("harita ogesi yok"); return 1; }

            var root = map.Address;
            while (UiFinder.Reread(h, root) is { ParentPtr: not 0 } e && UiFinder.Reread(h, e.ParentPtr) is not null) { root = e.ParentPtr; }

            var container = AltarLabels.Resolve(h, root, path);
            Console.WriteLine($"kok {root:X}  kap {string.Join('/', path)} = {container:X}");
            if (container == 0) { return 1; }

            // Bilinen varliklar: WorldItem varligi ve icindeki esya varligi -> aciklama.
            var area = TerrainFinder.Find(h, _ => { })
                .Select(t => t.StructAddress - GameLayout.TerrainInArea)
                .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
                .OrderByDescending(x => x.Count)
                .FirstOrDefault().Area;
            var known = new Dictionary<ulong, string>();
            foreach (var e in MinimapIconProbe.Collect(h, area))
            {
                if (!e.Info.Components.TryGetValue("WorldItem", out var world)) { continue; }

                var item = PlayerChain.ReadPointer(h, world + GroundUniqueReader.WorldItemEntity);
                var itemPath = EntityReader.ReadInfo(h, item, anyFlag: true)?.Path ?? "?";
                var tail = itemPath[(itemPath.LastIndexOf('/') + 1)..];
                var unique = GroundUniqueReader.Classify(h, e.Info) is { Length: > 0 } u ? $" = {u}" : "";
                known[e.Entity] = $"WorldItem({tail}{unique})";
                known[item] = $"esya({tail})";
            }

            Console.WriteLine($"{known.Count / 2} yer esyasi bilinen");

            // GroundLabelMap sinamasi: agactan gelen her (varlik, yazi) cifti; varlik bilinen bir WorldItem mi, yazinin metni.
            if (AltarLabels.Resolve(h, root, GroundLabelMap.ElementPath) is var labelsElement and not 0)
            {
                var pairs = GroundLabelMap.Read(h, labelsElement);
                var good = pairs.Count(kv => known.TryGetValue(kv.Key, out var w) && w.StartsWith("WorldItem", StringComparison.Ordinal));
                Console.WriteLine($"GroundLabelMap: {pairs.Count} cift, {good} tanesinin varligi bilinen WorldItem");
                foreach (var (entity, label) in pairs.Where(kv => known.GetValueOrDefault(kv.Key, "").Contains(" = ")).Concat(pairs.Take(3)))
                {
                    var vis = UiFinder.Reread(h, label) is { } el ? (el.Visible ? "gorunur" : "gizli") : "?";
                    Console.WriteLine($"    {known.GetValueOrDefault(entity, entity.ToString("X"))} -> \"{TextOf(h, label)}\" {vis}");
                }
            }
            var kids = Children(h, container);
            Console.WriteLine($"{kids.Count} yazi ogesi");

            // Kabin kendisinde: yazi ogesi ve varligi yan yana tutan dugumler (std::list / vector, 3 kademe).
            var labels = kids.ToHashSet();
            var nodes = 0;
            void Walk(ulong at, string chain, int depth, HashSet<ulong> visited)
            {
                var buf = new byte[depth == 3 ? Span : 0x60];
                if (!EntityReader.Read(h, at, buf)) { return; }

                for (var o = 0; o + 8 <= buf.Length; o += 8)
                {
                    var p = BitConverter.ToUInt64(buf, o);
                    if (labels.Contains(p) && depth < 3)
                    {
                        for (var j = 0; j + 8 <= buf.Length; j += 8)
                        {
                            if (known.TryGetValue(BitConverter.ToUInt64(buf, j), out var what) && nodes++ < 12)
                            {
                                Console.WriteLine($"  dugum {chain} ({at:X}): +{o:X}=yazi {p:X} \"{TextOf(h, p)}\"  +{j:X}={what}");
                            }
                        }
                    }

                    if (depth > 0 && PlayerChain.LooksLikePointer(p) && !labels.Contains(p) && !known.ContainsKey(p) && visited.Add(p))
                    {
                        Walk(p, $"{chain}+{o:X}->", depth - 1, visited);
                    }
                }
            }

            // Ucuncu arguman: aramanin basladigi oge (or. "1/6"); yoksa kabin kendisi.
            var start = args.Length > 2 ? AltarLabels.Resolve(h, root, args[2].Split('/').Select(int.Parse).ToArray()) : container;
            Walk(start, args.Length > 2 ? args[2] : "kap", 3, new HashSet<ulong> { start });
            Console.WriteLine($"{nodes} eslesen dugum");
            foreach (var label in kids.Take(count))
            {
                var text = TextOf(h, label);
                var hits = new List<string>();
                var buf = new byte[Span];
                if (EntityReader.Read(h, label, buf))
                {
                    for (var o = 0; o + 8 <= Span; o += 8)
                    {
                        var p = BitConverter.ToUInt64(buf, o);
                        if (known.TryGetValue(p, out var what)) { hits.Add($"+{o:X}={what}"); continue; }
                        if (!PlayerChain.LooksLikePointer(p)) { continue; }

                        var inner = new byte[0x100];
                        if (!EntityReader.Read(h, p, inner)) { continue; }

                        for (var i = 0; i + 8 <= inner.Length; i += 8)
                        {
                            if (known.TryGetValue(BitConverter.ToUInt64(inner, i), out var deep)) { hits.Add($"+{o:X}->+{i:X}={deep}"); }
                        }
                    }
                }

                var kidKids = Children(h, label);
                Console.WriteLine($"{label:X} \"{text}\" cocuk {kidKids.Count}  {string.Join("  ", hits)}");
                foreach (var k in kidKids.Take(3)) { Console.WriteLine($"    cocuk {k:X} \"{TextOf(h, k)}\""); }
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static List<ulong> Children(IntPtr h, ulong element)
    {
        var list = new List<ulong>();
        var head = new byte[0x10];
        if (!EntityReader.Read(h, element + 0xC0, head)) { return list; }

        var first = BitConverter.ToUInt64(head, 0);
        var last = BitConverter.ToUInt64(head, 8);
        if (first == 0 || last <= first || last - first > 8 * 4096) { return list; }

        var kids = new byte[(int)(last - first)];
        if (!EntityReader.Read(h, first, kids)) { return list; }

        for (var i = 0; i + 8 <= kids.Length; i += 8) { list.Add(BitConverter.ToUInt64(kids, i)); }
        return list;
    }

    private static string TextOf(IntPtr h, ulong element)
    {
        var buf = new byte[0x380];
        return EntityReader.Read(h, element, buf) ? EntityReader.ReadWString(h, buf, AltarLabels.TextOffset).Replace("\n", " ") : "";
    }
}
