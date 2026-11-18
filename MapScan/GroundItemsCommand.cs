namespace Poe2Map;

/// <summary>
///     MapScan grounditems [adet=8] [ad-parcasi]: yerdeki esyalari (WorldItem bileseni olan varliklar) en yakindan basar:
///     WorldItem bileseninin hangi ofsetinde esya varligi var, esyanin yolu ve bilesenleri, bilesenlerdeki "Art/" metinleri
///     (RenderItem resmi) ve Mods bileseninin harf iceren metinleri ile kucuk sayilari. Amac (18.11.2026, kullanici
///     istegi): tanimlanmamis unique'in (or. Heavy Belt -> Mageblood) adini bulmak. Sadece okur.
/// </summary>
internal static class GroundItemsCommand
{
    internal static int Run(string[] args)
    {
        var count = args.Length > 0 && int.TryParse(args[0], out var c) ? c : 8;
        var filter = args.Length > 1 ? args[1] : "";
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
                .FirstOrDefault().Area;
            var player = LocalPlayer.Resolve(h, area) is { } pr ? LocalPlayer.ReadPosition(h, pr.Render) : null;

            var items = new List<(double Dist, ulong World, ulong Item, int Offset, EntityReader.EntityInfo Info)>();
            foreach (var e in MinimapIconProbe.Collect(h, area))
            {
                if (!e.Info.Components.TryGetValue("WorldItem", out var world)) { continue; }

                var pos = e.Info.Components.TryGetValue("Render", out var r) ? LocalPlayer.ReadPosition(h, r) : null;
                var dist = pos is { } q && player is { } p ? Math.Sqrt(((q.X - p.X) * (q.X - p.X)) + ((q.Y - p.Y) * (q.Y - p.Y))) : 1e9;

                var buf = new byte[0x80];
                if (!EntityReader.Read(h, world, buf)) { continue; }

                for (var o = 0x10; o + 8 <= buf.Length; o += 8)
                {
                    var ptr = BitConverter.ToUInt64(buf, o);
                    if (!PlayerChain.LooksLikePointer(ptr) || ptr == e.Entity) { continue; }
                    if (EntityReader.ReadInfo(h, ptr) is { } info && info.Path.StartsWith("Metadata/Items/", StringComparison.Ordinal))
                    {
                        if (filter.Length == 0 || info.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        {
                            items.Add((dist, e.Entity, ptr, o, info));
                        }

                        break;
                    }
                }
            }

            // adet 0: hepsi tek satir - yol, RenderItem +0x28 resmi, Mods +0xA0..+0xC0 sayilari (nadirlik / tanimlilik aramak icin).
            if (count == 0)
            {
                foreach (var (dist, world, item, _, info) in items.OrderBy(i => i.Dist))
                {
                    // Overlay'in bu esyaya verdigi ad (GroundUniqueReader.Classify): "" unique degil, null okunamadi.
                    var named = EntityReader.ReadInfo(h, world) is { } wi ? GroundUniqueReader.Classify(h, wi) ?? "null" : "varlik okunamadi";
                    var art = info.Components.TryGetValue("RenderItem", out var ri) &&
                              PlayerChain.ReadPointer(h, ri + 0x28) is var ap && PlayerChain.LooksLikePointer(ap)
                        ? WispAoCommand.Text(h, ap, _ => true) ?? "?" : "-";
                    var mods = "";
                    if (info.Components.TryGetValue("Mods", out var m))
                    {
                        var raw = new byte[0x20];
                        if (EntityReader.Read(h, m + 0xA0, raw))
                        {
                            mods = string.Join(' ', Enumerable.Range(0, 8).Select(i => BitConverter.ToInt32(raw, i * 4)));
                        }
                    }

                    Console.WriteLine($"{dist,6:0}  {info.Path[(info.Path.LastIndexOf('/') + 1)..],-36} {art.Replace("Art/2DItems/", "")}  [{mods}]  ad=\"{named}\"");
                }

                return 0;
            }

            // ucuncu arguman: esyanin bilesenlerinde (3 kademe) bu metni iceren yazilari ara, or. unique'in adi.
            if (args.Length > 2)
            {
                var needle = args[2];
                foreach (var (dist, _, item, _, info) in items.OrderBy(i => i.Dist).Take(count))
                {
                    Console.WriteLine($"=== uzaklik {dist:0} {item:X} {info.Path}");
                    foreach (var (name, address) in info.Components)
                    {
                        WispAoCommand.Search(h, name, address, 0x300, 3, new HashSet<ulong> { item, address },
                            s => s.Contains(needle, StringComparison.OrdinalIgnoreCase));
                    }
                }

                return 0;
            }

            Console.WriteLine($"{items.Count} yer esyasi; en yakin {count} basiliyor");
            foreach (var (dist, world, item, offset, info) in items.OrderBy(i => i.Dist).Take(count))
            {
                Console.WriteLine($"=== uzaklik {dist:0}  WorldItem+{offset:X} -> {item:X}  {info.Path}");
                Console.WriteLine($"    bilesenler: {string.Join(' ', info.Components.Keys)}");
                foreach (var (name, address) in info.Components)
                {
                    WispAoCommand.Search(h, name, address, 0x200, 2, new HashSet<ulong> { item, address },
                        s => s.StartsWith("Art/", StringComparison.Ordinal));
                }

                if (info.Components.TryGetValue("Mods", out var mods))
                {
                    WispAoCommand.Search(h, "Mods", mods, 0x200, 2, new HashSet<ulong> { item, mods },
                        s => s.Any(char.IsLetter) && !s.StartsWith("Art/", StringComparison.Ordinal) && !s.StartsWith("Metadata/", StringComparison.Ordinal));
                    var raw = new byte[0x200];
                    if (EntityReader.Read(h, mods, raw))
                    {
                        var small = Enumerable.Range(0, raw.Length / 4)
                            .Select(i => (Off: i * 4, V: BitConverter.ToInt32(raw, i * 4)))
                            .Where(x => x.V is > 0 and < 100)
                            .Select(x => $"+{x.Off:X}={x.V}");
                        Console.WriteLine($"    Mods kucuk sayilar: {string.Join(' ', small)}");
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
