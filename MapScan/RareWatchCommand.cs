using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     MapScan rarewatch [regex = Fungal|Shaman] [dakika = 240]: canli alandaki rare / unique canavarlari ilk goruldukleri
///     anda basar (nadirlik, Render +0x148 adi, yol, can, konum, uyanik / uyuyan); adi ya da yolu regex'e uyan her
///     canavar (nadirligi ne olursa olsun) "&gt;&gt;&gt;" ile isaretlenir, oldugunde bir satir daha. 21.11.2026, kullanici:
///     "Fungal Shamans bu isimdeki rare monsterlari tespit etmelisin". Alan degisimi overlay'in alan tutucu tarifiyle
///     (data\area-holder.txt, sadece okunur) taramasiz izleniyor; tarif tutmazsa her 3 sn'de alan yeniden bulunur.
///     Sadece okur.
/// </summary>
internal static class RareWatchCommand
{
    private sealed class Seen
    {
        internal string Line = "";
        internal bool Match;
        internal bool Dead;
    }

    internal static int Run(string[] args)
    {
        var filter = new Regex(args.Length > 0 ? args[0] : "Fungal|Shaman", RegexOptions.IgnoreCase);
        var minutes = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 240;
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess basarisiz."); return 1; }

        try
        {
            var clock = Stopwatch.StartNew();
            var area = 0UL;
            var holders = new List<(ulong Object, int Back)>();
            var seen = new Dictionary<ulong, Seen>();
            var lastFind = -10_000L;
            var rares = 0;
            var matches = 0;

            while (clock.Elapsed.TotalMinutes < minutes)
            {
                var now = clock.ElapsedMilliseconds;
                var current = Current(h, holders);
                if (current == 0 && now - lastFind > 3000)
                {
                    lastFind = now;
                    current = Find(h);
                    holders = Holders(h, current);
                }

                if (current != 0 && current != area)
                {
                    area = current;
                    seen.Clear();
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss}  alan {area:X}  ({AreaIdentity.TerrainPath(h, area) ?? "?"})  tutucu {holders.Count}");
                }

                if (area != 0) { Scan(h, area, filter, seen, ref rares, ref matches); }
                if (now % 60_000 < 300) { Console.WriteLine($"{DateTime.Now:HH:mm:ss}  ozet: {rares} rare/unique, {matches} eslesme"); }
                Thread.Sleep(250);
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static void Scan(IntPtr h, ulong area, Regex filter, Dictionary<ulong, Seen> seen, ref int rares, ref int matches)
    {
        foreach (var (list, offset) in new[] { ("uyanik", EntityReader.AwakeMap), ("uyuyan", EntityReader.SleepingMap) })
        {
            if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }

            foreach (var (_, entity) in EntityReader.WalkMap(h, map, 5000))
            {
                if (seen.TryGetValue(entity, out var known))
                {
                    if (known.Match && !known.Dead && Life(h, entity) is (> 0, <= 0))
                    {
                        known.Dead = true;
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  >>> oldu  {known.Line}");
                    }

                    continue;
                }

                if (EntityReader.ReadInfo(h, entity) is not { IsMonster: true } info || !info.Components.ContainsKey("Monster")) { continue; }

                // Bayat kopya (ayni konum ve can, yeni adres; 21:27 canlida ayni Fungal Shaman 2-3 kez basildi): Render
                // bileseni bu varliga ait degilse sayilmaz (EntityReader.TryReadMonster ile ayni sinav).
                if (!info.Components.TryGetValue("Render", out var render) || !EntityReader.OwnedBy(h, render, entity)) { continue; }

                var rarity = Rarity(h, info);
                var name = MapIconReader.DisplayName(h, info);
                var match = filter.IsMatch(name) || filter.IsMatch(info.Path);
                var entry = new Seen { Match = match };
                seen[entity] = entry;
                if (rarity < 2 && !match) { continue; }

                var (total, hp) = Life(h, entity) ?? (0, 0);
                var pos = Position(h, info);
                entry.Line = $"{RarityName(rarity),-6} \"{name}\"  {info.Path}  can {hp}/{total}  konum {pos}  {list}  {entity:X}";
                entry.Dead = total > 0 && hp <= 0;
                if (rarity >= 2) { rares++; }
                if (match) { matches++; }
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {(match ? ">>> " : "    ")}{entry.Line}");
            }
        }
    }

    /// <summary>
    ///     MapScan rareprobe [ad = Fungal Shaman]: o adli her kayit (bayrak sayilmadan okunur) icin overlay'in sinavlari
    ///     tek tek: +0x8C bayti, normal ReadInfo, Monster bileseni, Render sahipligi, can, tepki, TryReadMonster.
    /// </summary>
    internal static int Probe(string[] args)
    {
        var wanted = args.Length > 0 ? args[0] : "Fungal Shaman";
        var game = Native.FindGame();
        if (game == null) { Console.Error.WriteLine("Oyun sureci bulunamadi."); return 1; }

        var h = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        try
        {
            var area = Find(h);
            for (var round = 0; round < 20; round++)
            {
                foreach (var (list, offset) in new[] { ("uyanik", EntityReader.AwakeMap), ("uyuyan", EntityReader.SleepingMap) })
                {
                    if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }

                    foreach (var (id, entity) in EntityReader.WalkMap(h, map, 5000))
                    {
                        if (EntityReader.ReadInfo(h, entity, anyFlag: true) is not { IsMonster: true } any) { continue; }
                        if (!string.Equals(MapIconReader.DisplayName(h, any), wanted, StringComparison.OrdinalIgnoreCase)) { continue; }

                        var flag = new byte[1];
                        EntityReader.Read(h, entity + 0x8C, flag);
                        var normal = EntityReader.ReadInfo(h, entity);
                        var owned = any.Components.TryGetValue("Render", out var render) && EntityReader.OwnedBy(h, render, entity);
                        var reaction = new byte[1];
                        var react = any.Components.TryGetValue("Positioned", out var pos) && EntityReader.Read(h, pos + EntityReader.Reaction, reaction) ? $"{reaction[0]:X2}" : "-";
                        var ok = normal is not null && EntityReader.TryReadMonster(h, entity, normal, out _);
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss.f} {list} id {id} {entity:X}  8C={flag[0]:X2}  ReadInfo {(normal is null ? "NULL" : "ok")}  Monster {any.Components.ContainsKey("Monster")}  sahip {owned}  can {Life(h, entity)}  tepki {react}  konum {Position(h, any)}  TryReadMonster {ok}");
                    }
                }

                Console.WriteLine("--");
                Thread.Sleep(500);
            }

            return 0;
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    private static string RarityName(int r) => r switch { 0 => "normal", 1 => "magic", 2 => "RARE", 3 => "UNIQUE", _ => $"r{r}" };

    private static int Rarity(IntPtr h, EntityReader.EntityInfo info)
    {
        if (!info.Components.TryGetValue("ObjectMagicProperties", out var magic)) { return -1; }
        var b = new byte[4];
        return EntityReader.Read(h, magic + EntityReader.RarityOffset, b) ? BitConverter.ToInt32(b, 0) : -1;
    }

    private static (int Total, int Current)? Life(IntPtr h, ulong entity)
    {
        if (EntityReader.ReadInfo(h, entity) is not { } info || !info.Components.TryGetValue("Life", out var life)) { return null; }
        var b = new byte[8];
        return EntityReader.Read(h, life + EntityReader.LifeTotal, b) ? (BitConverter.ToInt32(b, 0), BitConverter.ToInt32(b, 4)) : null;
    }

    private static string Position(IntPtr h, EntityReader.EntityInfo info)
    {
        if (!info.Components.TryGetValue("Render", out var render)) { return "?"; }
        var b = new byte[8];
        return EntityReader.Read(h, render + EntityReader.RenderPosition, b)
            ? $"({BitConverter.ToSingle(b, 0):F0}, {BitConverter.ToSingle(b, 4):F0})"
            : "?";
    }

    /// <summary>Alan tutucularin gosterdigi alan; hepsi ayni degilse ya da hic yoksa 0.</summary>
    private static ulong Current(IntPtr h, List<(ulong Object, int Back)> holders)
    {
        var values = holders.Select(x => PlayerChain.ReadPointer(h, x.Object + (ulong)x.Back)).Where(PlayerChain.LooksLikePointer).Distinct().ToList();
        return values.Count == 1 ? values[0] : 0;
    }

    /// <summary>Overlay'in tarif dosyasindaki (yuva, geri) ciftlerinden bu alanda tutanlar: alan + yuva -&gt; nesne, nesne + geri == alan.</summary>
    private static List<(ulong Object, int Back)> Holders(IntPtr h, ulong area)
    {
        var result = new List<(ulong, int)>();
        if (area == 0) { return result; }

        var file = Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\MapOverlay\bin\Debug\net10.0-windows\data\area-holder.txt");
        if (!File.Exists(file)) { return result; }

        foreach (var line in File.ReadAllLines(file).Where(l => l.StartsWith("recipe=", StringComparison.Ordinal)))
        {
            var parts = line["recipe=".Length..].Split(':');
            if (parts.Length != 2) { continue; }
            var slot = Convert.ToInt32(parts[0], 16);
            var back = Convert.ToInt32(parts[1], 16);
            var obj = PlayerChain.ReadPointer(h, area + (ulong)slot);
            if (PlayerChain.LooksLikePointer(obj) && obj != area && PlayerChain.ReadPointer(h, obj + (ulong)back) == area) { result.Add((obj, back)); }
        }

        return result;
    }

    /// <summary>Canli alan: oyuncusu kendi izgarasinda yurunebilir olan alan (LiveArea); bulunamazsa en cok uyanik varligi olan.</summary>
    private static ulong Find(IntPtr h)
    {
        if (LiveArea.FindLive(h, 0) is { } t) { return t.StructAddress - GameLayout.TerrainInArea; }

        return TerrainFinder.Find(h, _ => { })
            .Select(x => x.StructAddress - GameLayout.TerrainInArea)
            .Select(a => (Area: a, Count: EntityReader.ReadMapHeader(h, a + EntityReader.AwakeMap)?.Size ?? 0))
            .OrderByDescending(x => x.Count)
            .FirstOrDefault().Area;
    }
}
