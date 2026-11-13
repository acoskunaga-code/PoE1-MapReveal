using System.Text;

namespace Poe2Map;

/// <summary>
///     MapScan minimapicons [ornek-sayisi]: canli alandaki varliklarin MinimapIcon bilesenini okur ve simge
///     kimliginin hangi alanda durdugunu bulmak icin ham bellegi aciklamali basar. Sadece okur.
///
///     Hicbir offset varsayilmiyor. Bilesenin ilk 0x80 baytindaki her sekiz baytlik alan icin: deger, iki
///     tamsayi hali, gosterdigi metin (ASCII / UTF-16) ve bir kademe daha gidilince bulunan metin yazilir.
///     Sonra her dort baytlik alanin butun bilesenlerdeki deger dagilimi basilir: bir simge kimligi olan
///     alan az sayida farkli deger alir ve ayni yolu paylasan varliklarda hep ayni kalir.
/// </summary>
internal static class MinimapIconProbe
{
    private const int Window = 0x80;
    private const string ComponentName = "MinimapIcon";

    internal sealed record Entry(bool Awake, ulong Entity, EntityReader.EntityInfo Info);

    internal static int Run(string[] args)
    {
        var samples = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 25;

        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var terrains = TerrainFinder.Find(h, _ => { });
            var area = terrains
                .Select(t => t.StructAddress - GameLayout.TerrainInArea)
                .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
                .OrderByDescending(x => x.Count)
                .FirstOrDefault();

            if (area.Count == 0) { Console.WriteLine("Canli alanda varlik yok."); return 1; }

            var entries = Collect(h, area.Area);
            Console.WriteLine($"canli alan {area.Area:X}  varlik {entries.Count} (uyanik {entries.Count(e => e.Awake)})");

            ComponentNames(entries);

            var icons = entries.Where(e => e.Info.Components.ContainsKey(ComponentName)).ToList();
            Console.WriteLine();
            Console.WriteLine($"{ComponentName} bileseni olan varlik: {icons.Count} (uyanik {icons.Count(e => e.Awake)})");
            if (icons.Count == 0) { return 1; }

            var perPath = icons.GroupBy(e => e.Info.Path).OrderByDescending(g => g.Count()).ToList();
            Console.WriteLine($"{perPath.Count} farkli yol; oyuncu {PlayerText(h, entries)}");
            foreach (var e in icons.OrderBy(e => e.Awake ? 0 : 1).Take(120))
            {
                var row = PlayerChain.ReadPointer(h, e.Info.Components[ComponentName] + 0x20);
                var id = InRange(row) && EntityReader.Read(h, row, new byte[8]) ? RowId(h, row) : "?";
                var owned = (EntityReader.OwnedBy(h, e.Info.Components[ComponentName], e.Entity) ? "i" : "-") +
                            (e.Info.Components.TryGetValue("Render", out var r) && EntityReader.OwnedBy(h, r, e.Entity) ? "r" : "-");
                // +0x3C: acilan strongbox'ta 0 -> 1 (12.11.2026 izleme); gizli bayragi adayi.
                var hidden = new byte[4];
                var flag = EntityReader.Read(h, e.Info.Components[ComponentName] + 0x3C, hidden) ? BitConverter.ToInt32(hidden, 0).ToString() : "?";
                var name = MinimapIconWatch.RenderName(h, e.Info);
                Console.WriteLine($"  {(e.Awake ? "A" : "S")} {owned} 3C={flag,-2} {id,-24} {PositionText(h, e)}  \"{name}\"  {e.Info.Path}");
            }

            Console.WriteLine();
            foreach (var g in perPath.Take(samples))
            {
                var e = g.First();
                var component = e.Info.Components[ComponentName];
                Console.WriteLine($"--- {g.Key}  ({(e.Awake ? "uyanik" : "uyuyan")}, bilesen {component:X}, {g.Count()} adet)");
                Dump(h, e.Entity, component);
                Console.WriteLine("  satir: " + Row(h, component));
            }

            DatObject(h, icons[0].Info.Components[ComponentName]);
            Statistics(h, icons);
            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    internal static List<Entry> Collect(IntPtr h, ulong area)
    {
        var list = new List<Entry>();
        foreach (var (offset, awake) in new[] { (EntityReader.AwakeMap, true), (EntityReader.SleepingMap, false) })
        {
            if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }

            foreach (var (_, entity) in EntityReader.WalkMap(h, map))
            {
                if (EntityReader.ReadInfo(h, entity) is { } info) { list.Add(new Entry(awake, entity, info)); }
            }
        }

        return list;
    }

    private static void ComponentNames(List<Entry> entries)
    {
        var names = entries
            .SelectMany(e => e.Info.Components.Keys)
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}x{g.Count()}")
            .ToList();
        Console.WriteLine($"{names.Count} bilesen adi: {string.Join("  ", names)}");
    }

    private static void Dump(IntPtr h, ulong entity, ulong component)
    {
        var buf = new byte[Window];
        if (!EntityReader.Read(h, component, buf)) { Console.WriteLine("  okunamadi"); return; }

        for (var o = 0; o < Window; o += 8)
        {
            var q = BitConverter.ToUInt64(buf, o);
            var lo = BitConverter.ToInt32(buf, o);
            var hi = BitConverter.ToInt32(buf, o + 4);
            Console.WriteLine($"  +{o:X2}  {q:X16}  {lo,11} {hi,11}  {Describe(h, q, entity)}");
        }
    }

    /// <summary>
    ///     Bilesenin +0x20 alani dat satirini gosteriyor ve satirlar 34 bayt aralikli, yani adres hizali degil;
    ///     bu yuzden hizasiz ama makul bir adres de takip ediliyor.
    /// </summary>
    internal static bool InRange(ulong v) => v >= 0x10000 && v <= 0x7FFFFFFFFFFF;

    /// <summary>Satirin ilk alani: Id metni (UTF-16) isaretcisi.</summary>
    internal static string RowId(IntPtr h, ulong row)
    {
        var buf = new byte[8];
        return EntityReader.Read(h, row, buf) ? Text(h, BitConverter.ToUInt64(buf, 0)).Trim('"', 'w', 'a') : "?";
    }

    private static string PositionText(IntPtr h, Entry e)
    {
        if (!e.Info.Components.TryGetValue("Render", out var render)) { return "Render yok            "; }
        var buf = new byte[12];
        if (!EntityReader.Read(h, render + EntityReader.RenderPosition, buf)) { return "okunamadi            "; }
        return $"({BitConverter.ToSingle(buf, 0),7:F0},{BitConverter.ToSingle(buf, 4),7:F0})";
    }

    private static string PlayerText(IntPtr h, List<Entry> entries)
    {
        var player = entries.FirstOrDefault(e => e.Info.Components.ContainsKey("Player"));
        return player is null ? "yok" : PositionText(h, player);
    }

    /// <summary>Dat satirinin okunabilir hali: ilk alan metin isaretcisi, geri kalan bayt dokumu.</summary>
    private static string Row(IntPtr h, ulong component)
    {
        var row = PlayerChain.ReadPointer(h, component + 0x20);
        if (!InRange(row)) { return "gecersiz"; }

        var buf = new byte[0x30];
        if (!EntityReader.Read(h, row, buf)) { return $"{row:X} okunamadi"; }

        var id = Text(h, BitConverter.ToUInt64(buf, 0));
        return $"{row:X}  Id {id}  bayt+08: {Convert.ToHexString(buf, 8, 0x1A)}";
    }

    /// <summary>Bilesenin +0x28 alani: dat dosyasi nesnesi. Satir dizisinin nerede basladigini aramak icin dokulur.</summary>
    private static void DatObject(IntPtr h, ulong component)
    {
        var dat = PlayerChain.ReadPointer(h, component + 0x28);
        Console.WriteLine();
        Console.WriteLine($"=== dat nesnesi {dat:X} ===");
        var buf = new byte[0x100];
        if (!EntityReader.Read(h, dat, buf)) { Console.WriteLine("okunamadi"); return; }

        for (var o = 0; o < buf.Length; o += 8)
        {
            var q = BitConverter.ToUInt64(buf, o);
            if (q == 0) { continue; }

            Console.WriteLine($"  +{o:X2}  {q:X16}  {BitConverter.ToInt32(buf, o),11} {BitConverter.ToInt32(buf, o + 4),11}  {Describe(h, q, 0)}");
        }
    }

    private static string Describe(IntPtr h, ulong value, ulong entity)
    {
        if (value == 0) { return ""; }
        if (value == entity) { return "= varlik"; }
        if (!InRange(value)) { return ""; }

        var text = Text(h, value);
        if (text.Length > 0) { return "-> " + text; }

        var target = new byte[0x40];
        if (!EntityReader.Read(h, value, target)) { return "-> (okunamadi)"; }

        var parts = new List<string>();
        for (var i = 0; i < target.Length; i += 8)
        {
            var inner = BitConverter.ToUInt64(target, i);
            if (!PlayerChain.LooksLikePointer(inner)) { continue; }

            var s = Text(h, inner);
            if (s.Length > 0) { parts.Add($"+{i:X2} {s}"); }
        }

        var ints = $"[{BitConverter.ToInt32(target, 0)} {BitConverter.ToInt32(target, 4)} {BitConverter.ToInt32(target, 8)} {BitConverter.ToInt32(target, 12)}]";
        return "-> " + ints + (parts.Count > 0 ? "  " + string.Join("  ", parts) : "");
    }

    /// <summary>Adreste okunabilir bir ASCII ya da UTF-16 metin varsa tirnakli dondurur, yoksa "".</summary>
    internal static string Text(IntPtr h, ulong address)
    {
        var ascii = EntityReader.ReadAscii(h, address, 64);
        if (ascii.Length >= 2) { return $"a\"{ascii}\""; }

        var buf = new byte[128];
        if (!EntityReader.Read(h, address, buf)) { return ""; }

        var sb = new StringBuilder();
        for (var i = 0; i + 1 < buf.Length; i += 2)
        {
            var c = (char)(buf[i] | (buf[i + 1] << 8));
            if (c == 0) { break; }
            if (c < 0x20 || c > 0x7E) { return ""; }
            sb.Append(c);
        }

        return sb.Length >= 2 ? $"w\"{sb}\"" : "";
    }

    /// <summary>
    ///     Dort baytlik alan basina deger dagilimi. Kimlik alani az sayida farkli deger alir; her yol icin tek
    ///     degeri vardir (bir yoldaki butun varliklarda ayni).
    /// </summary>
    private static void Statistics(IntPtr h, List<Entry> icons)
    {
        var windows = icons
            .Select(e => (Entry: e, Data: Read(h, e.Info.Components[ComponentName])))
            .Where(x => x.Data != null)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"=== alan istatistigi ({windows.Count} bilesen) ===");
        Console.WriteLine("offset  farkli  yolBasinaTek  en sik degerler");

        for (var o = 0x08; o + 4 <= Window; o += 4)
        {
            var values = windows.Select(w => BitConverter.ToInt32(w.Data!, o)).ToList();
            var distinct = values.Distinct().Count();
            if (distinct == 1 && values[0] == 0) { continue; }

            // Ayni yoldaki varliklarin hepsi ayni degeri veriyor mu?
            var stable = windows.GroupBy(w => w.Entry.Info.Path)
                .Count(g => g.Select(w => BitConverter.ToInt32(w.Data!, o)).Distinct().Count() == 1);
            var paths = windows.Select(w => w.Entry.Info.Path).Distinct().Count();

            var top = values.GroupBy(v => v).OrderByDescending(g => g.Count()).Take(6)
                .Select(g => $"{g.Key}x{g.Count()}");
            Console.WriteLine($"+{o:X2}     {distinct,6}  {stable,4}/{paths,-4}      {string.Join("  ", top)}");
        }
    }

    private static byte[]? Read(IntPtr h, ulong component)
    {
        var buf = new byte[Window];
        return EntityReader.Read(h, component, buf) ? buf : null;
    }
}
