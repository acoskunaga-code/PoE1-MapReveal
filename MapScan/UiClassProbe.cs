namespace Poe2Map;

/// <summary>
///     UI agacini SINIF sinif ayirarak bulur; ebeveyn alanini ve cocuk vektorunu gozlemden cikarir.
///
///     Neden: "+0x08 == kendi adresi" kumesi (~67k nesne) UI olmayan cok sey tasiyor (render durum tablolari,
///     doku kayitlari...). Hepsini birlikte oylamak (uilearn) gurultulu cikti. Her nesnenin +0x00'i bir C++
///     tipinin sanal tablosu; ayni sanal tabloyu paylasanlar ayni tip. Tip bazinda oylama temiz:
///
///         E'nin bir alani P'yi gosteriyor VE P'nin (bas, son) vektorundeki diziyi canli okuyunca E icinde
///         cikiyorsa, (alan, vektor) cifti bir ebeveyn / cocuk kenaridir.
///
///     UI tipinin ornekleri hep ayni (alan, vektor) ciftiyle baglanir; UI olmayanlar baglanmaz ya da baska
///     bir ciftle baglanir. Cikan ortak cift = UI ogesinin ebeveyn alani ve cocuk vektoru ofseti. Sonra o
///     cift ile orman kurulur: en buyuk kok oyun arayuzudur, cocuklari ana panellerdir.
///
///         uiclass [goruntu]    (verilmezse canli goruntu alinir, %TEMP%\poe1-ui-live.bin'e yazilir)
/// </summary>
internal static class UiClassProbe
{
    private const int MaxParentSlot = 0x400;
    private const int MaxVectorSlot = 0x100;
    private const int MaxChildren = 1000;
    private const int MinClassSize = 20;

    private static string LivePath => Path.Combine(Path.GetTempPath(), "poe1-ui-live.bin");

    private static string SetPath => Path.Combine(Path.GetTempPath(), "poe1-ui-set.txt");

    internal static int Run(string[] args)
    {
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var moduleBase = (ulong)game.MainModule!.BaseAddress.ToInt64();
            var moduleSize = (ulong)game.MainModule.ModuleMemorySize;

            UiSnapshot snap;
            if (args.Length > 0 && args[0].StartsWith("canli:", StringComparison.OrdinalIgnoreCase))
            {
                snap = UiSnapshot.Take(h, Convert.ToInt32(args[0][6..], 16));
            }
            else if (args.Length > 0 && File.Exists(args[0]))
            {
                snap = UiSnapshot.Load(args[0]);
            }
            else
            {
                snap = ClassSnapshot.Take(h, moduleBase, moduleBase + moduleSize, MinClassSize);
                snap.Save(LivePath);
            }

            Console.WriteLine($"{snap.Elements.Count} oge");
            var graph = new Graph(h, snap);

            var classes = snap.Elements
                .GroupBy(kv => BitConverter.ToUInt64(kv.Value, 0))
                .Where(g => g.Key >= moduleBase && g.Key < moduleBase + moduleSize && g.Count() >= MinClassSize)
                .OrderByDescending(g => g.Count())
                .ToList();
            Console.WriteLine($"sanal tablosu modulde ve en az {MinClassSize} ornegi olan sinif: {classes.Count} " +
                              $"({classes.Sum(g => g.Count())} oge)\n");

            var global = new Dictionary<(int, int), int>();
            var shown = 0;
            foreach (var cls in classes)
            {
                var votes = new Dictionary<(int, int), int>();
                var attached = 0;
                foreach (var (e, raw) in cls)
                {
                    var edges = graph.Attach(e, raw);
                    if (edges.Count == 0) { continue; }

                    attached++;
                    foreach (var edge in edges) { votes[edge] = votes.GetValueOrDefault(edge) + 1; }
                }

                var top = votes.OrderByDescending(v => v.Value).FirstOrDefault();
                if (attached * 2 >= cls.Count() && top.Value > 0)
                {
                    global[top.Key] = global.GetValueOrDefault(top.Key) + top.Value;
                }

                if (shown++ < 40)
                {
                    Console.WriteLine($"  vt+0x{cls.Key - moduleBase:X}  {cls.Count(),5} ornek  baglanan {attached,5}  " +
                                      (top.Value > 0 ? $"en cok oy: ebeveyn +0x{top.Key.Item1:X}, vektor +0x{top.Key.Item2:X} x{top.Value}" : "baglanti yok"));
                }
            }

            Console.WriteLine("\nsiniflar arasi ortak (ebeveyn alani, cocuk vektoru) oylari:");
            var ranked = global.OrderByDescending(v => v.Value).Take(6).ToList();
            foreach (var (pair, count) in ranked) { Console.WriteLine($"  ebeveyn +0x{pair.Item1:X}, vektor +0x{pair.Item2:X}   {count} oy"); }

            if (ranked.Count == 0) { Console.WriteLine("hicbir sinif agaca baglanmadi"); return 0; }

            foreach (var (pair, count) in ranked)
            {
                Console.WriteLine($"\n=== aday cift: ebeveyn +0x{pair.Item1:X}, vektor +0x{pair.Item2:X}  ({count} oy) ===");
                graph.Extend(pair);
                Forest(graph, moduleBase, pair);
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    /// <summary>Kazanan cift ile ormani kurar, en buyuk kokun ilk katmanini yazdirir, kumeyi diske yazar.</summary>
    private static void Forest(Graph g, ulong moduleBase, (int Parent, int Vector) pair)
    {
        var kids = new Dictionary<ulong, List<ulong>>();
        var hasParent = new HashSet<ulong>();
        foreach (var (p, pr) in g.Snap.Elements)
        {
            var vec = Graph.VectorAt(pr, pair.Vector);
            if (vec == null) { continue; }

            var items = g.Children(vec.Value.First, vec.Value.Last);
            if (items == null) { continue; }

            var list = items.Where(c => g.Snap.Elements.TryGetValue(c, out var cr) &&
                                        BitConverter.ToUInt64(cr, pair.Parent) == p).ToList();
            if (list.Count == 0) { continue; }

            kids[p] = list;
            foreach (var c in list) { hasParent.Add(c); }
        }

        var nodes = new HashSet<ulong>(kids.Keys);
        nodes.UnionWith(hasParent);
        var sizes = new Dictionary<ulong, int>();
        int Size(ulong n, int depth)
        {
            if (sizes.TryGetValue(n, out var cached)) { return cached; }

            if (depth > 64) { return 1; }

            sizes[n] = 1;
            var total = 1;
            if (kids.TryGetValue(n, out var list)) { foreach (var c in list) { total += Size(c, depth + 1); } }

            return sizes[n] = total;
        }

        var roots = kids.Keys.Where(k => !hasParent.Contains(k)).OrderByDescending(k => Size(k, 0)).ToList();
        Console.WriteLine($"\nUI ormani (ebeveyn +0x{pair.Parent:X}, vektor +0x{pair.Vector:X}): {nodes.Count} oge, {roots.Count} kok");
        foreach (var r in roots.Take(4))
        {
            Console.WriteLine($"  kok 0x{r:X}  {Rva(g, r, moduleBase)}  cocuk {kids[r].Count}  alt agac {Size(r, 0)}");
        }

        if (roots.Count > 0 && Size(roots[0], 0) >= 30)
        {
            Console.WriteLine($"\nen buyuk kokun cocuklari (0x{roots[0]:X}):");
            foreach (var c in kids[roots[0]].Take(40))
            {
                var n = kids.TryGetValue(c, out var cl) ? cl.Count : 0;
                Console.WriteLine($"    0x{c:X}  {Rva(g, c, moduleBase)}  cocuk {n,3}  alt agac {Size(c, 0)}");
            }
        }

        File.WriteAllLines(SetPath, new[] { $"ebeveyn=0x{pair.Parent:X} vektor=0x{pair.Vector:X}" }
            .Concat(nodes.Select(n => $"0x{n:X}")));
        Console.WriteLine($"\nUI kumesi yazildi: {SetPath}");
    }

    /// <summary>Sanal tablo RVA'si ve nesne basindaki +0x08 degeri (UI ogelerinde ayirma boyutu gibi duruyor).</summary>
    private static string Rva(Graph g, ulong element, ulong moduleBase) =>
        g.Snap.Elements.TryGetValue(element, out var raw)
            ? $"vt+0x{BitConverter.ToUInt64(raw, 0) - moduleBase:X} boyut {BitConverter.ToUInt64(raw, 8):X}"
            : "vt?";

    /// <summary>Goruntu uzerinde ebeveyn / cocuk sorgulari; cocuk dizileri canli okunur ve onbelleklenir.</summary>
    private sealed class Graph
    {
        private readonly IntPtr handle;
        private readonly Dictionary<(ulong, ulong), ulong[]?> arrays = new();

        internal Graph(IntPtr handle, UiSnapshot snap)
        {
            this.handle = handle;
            this.Snap = snap;
        }

        internal UiSnapshot Snap { get; }

        internal static (ulong First, ulong Last)? VectorAt(byte[] raw, int slot)
        {
            if (slot + 16 > raw.Length) { return null; }

            var first = BitConverter.ToUInt64(raw, slot);
            var last = BitConverter.ToUInt64(raw, slot + 8);
            if (!PlayerChain.LooksLikePointer(first) || last <= first || ((last - first) & 7) != 0) { return null; }

            return (last - first) / 8 > MaxChildren ? null : (first, last);
        }

        internal ulong[]? Children(ulong first, ulong last)
        {
            if (this.arrays.TryGetValue((first, last), out var cached)) { return cached; }

            var count = (int)((last - first) / 8);
            var bytes = new byte[count * 8];
            ulong[]? items = null;
            if (EntityReader.Read(this.handle, first, bytes))
            {
                items = new ulong[count];
                Buffer.BlockCopy(bytes, 0, items, 0, bytes.Length);
            }

            this.arrays[(first, last)] = items;
            return items;
        }

        /// <summary>
        ///     Tekil siniflari (arayuz koku, ara kaplar) goruntuye ekler: her ogenin ebeveyni ve cocuklari, kazanan
        ///     (ebeveyn alani, cocuk vektoru) cifti ile DOGRULANARAK canli okunur - ebeveynin dizisinde ogenin
        ///     kendisi, cocugun ebeveyn alaninda ogenin adresi bulunmali.
        /// </summary>
        internal void Extend((int Parent, int Vector) pair)
        {
            var queue = new Queue<ulong>(this.Snap.Elements.Keys);
            var added = 0;
            while (queue.Count > 0 && this.Snap.Elements.Count < 400000)
            {
                var e = queue.Dequeue();
                var raw = this.Snap.Elements[e];

                var p = BitConverter.ToUInt64(raw, pair.Parent);
                if (PlayerChain.LooksLikePointer(p) && !this.Snap.Elements.ContainsKey(p) && this.TryWindow(p, out var pr))
                {
                    var pv = VectorAt(pr, pair.Vector);
                    var siblings = pv == null ? null : this.Children(pv.Value.First, pv.Value.Last);
                    if (siblings != null && Array.IndexOf(siblings, e) >= 0)
                    {
                        this.Snap.Elements[p] = pr;
                        queue.Enqueue(p);
                        added++;
                    }
                }

                var v = VectorAt(raw, pair.Vector);
                var kids = v == null ? null : this.Children(v.Value.First, v.Value.Last);
                if (kids == null) { continue; }

                foreach (var c in kids)
                {
                    if (!PlayerChain.LooksLikePointer(c) || this.Snap.Elements.ContainsKey(c) || !this.TryWindow(c, out var cr) ||
                        BitConverter.ToUInt64(cr, pair.Parent) != e)
                    {
                        continue;
                    }

                    this.Snap.Elements[c] = cr;
                    queue.Enqueue(c);
                    added++;
                }
            }

            Console.WriteLine($"\nagac genisletildi: {added} oge eklendi (tekil siniflar, kok, ara kaplar)");
        }

        private bool TryWindow(ulong address, out byte[] raw)
        {
            raw = new byte[UiSnapshot.Window];
            return EntityReader.Read(this.handle, address, raw);
        }

        /// <summary>E'nin hangi (ebeveyn alani, ebeveynin vektoru) ciftleriyle bir ebeveynin cocuk dizisinde gectigi.</summary>
        internal List<(int Parent, int Vector)> Attach(ulong e, byte[] raw)
        {
            var edges = new List<(int, int)>();
            for (var a = 0x10; a + 8 <= Math.Min(raw.Length, MaxParentSlot); a += 8)
            {
                var p = BitConverter.ToUInt64(raw, a);
                if (p == e || !this.Snap.Elements.TryGetValue(p, out var pr)) { continue; }

                for (var b = 0x10; b <= MaxVectorSlot; b += 8)
                {
                    var vec = VectorAt(pr, b);
                    if (vec == null) { continue; }

                    var items = this.Children(vec.Value.First, vec.Value.Last);
                    if (items != null && Array.IndexOf(items, e) >= 0) { edges.Add((a, b)); }
                }
            }

            return edges;
        }
    }
}
