namespace Poe2Map;

/// <summary>
///     Alan degisiminde yeni alan nesnesini TARAMADAN bulur: alanin gosterdigi, iki ayri alanda AYNI adreste duran
///     ve o anki alana geri bakan kalici nesneler (oyun durumu gibi tekil nesneler).
///
///     Canlida bulundu (MapScan ingame, 27.10.2026): 0x523F4552410 (+0x218), 0x523F4500200 (+0xA0) ve 0x523F46E0B00 (+0x28)
///     iki farkli alanda ayni adresteydi ve her seferinde o anki alana bakiyordu; alanin kendi blogundaki ara nesneler ve
///     oyuncu varligi ise her alanda degisiyordu. Eski yontem (PoE2'den kalma: eski alan yapilarindan oyuncuyu bulma)
///     PoE1'de hic tutmadi: 8 sn bekleyip 5 sn bellek taramasina dusuyor, her alan degisimi 13 sn suruyordu.
///
///     Ogrenme (kendini dogrulayan, sabit sayi yok):
///       1. bir alan bulunca: alanin ilk 0x1200 baytindaki isaretcilerin gosterdigi, alanin KENDI blogunun disinda
///          (> 0x4000 uzakta) olan ve icinde alanin adresini tutan bir alan bulunan nesneler = ADAY (yuva, nesne, geri alan)
///       2. sonraki alanda adaylar o alani da gosteriyorsa KALICI: adresleri iki alanda ayni, nesne tekil
///       3. kalici nesnelerin (alandaki yuva, geri alan) TARIFI dosyaya yazilir; sonraki oturumlar ilk alanda tarifle
///          dogrudan kalici nesneyi bulur. Yamadan sonra tarif tutmazsa dogrulama basarisiz olur ve yeniden ogrenilir.
///
///     Kullanim: Observe(h, alan) her basarili konumlamadan sonra; Current(h) alan degisimini beklerken ve sonra.
/// </summary>
internal static class AreaHolder
{
    private const int AreaWindow = 0x1200;
    private const int HolderWindow = 0x800;
    private const long OwnBlock = 0x4000;
    private const int MaxRecipes = 8;

    private readonly record struct Holder(int Slot, ulong Object, int Back);

    private static readonly object Gate = new();
    private static readonly List<Holder> Live = new();
    private static List<Holder> pending = new();
    private static List<(int Slot, int Back)> recipes = new();
    private static bool loaded;

    /// <summary>Tanilama gunlugu; uygulama kendi gunlugune baglar.</summary>
    internal static Action<string>? Log { get; set; }

    internal static string ConfigPath => AppPaths.File("area-holder.txt");

    /// <summary>Dogrulanmis kalici nesne var mi: hizli alan degisimi mumkun.</summary>
    internal static bool Ready
    {
        get { lock (Gate) { return Live.Count > 0; } }
    }

    /// <summary>
    ///     Kalici nesnelerin gosterdigi alan adresi; farkliysa en cok oyu alan. Hicbiri gecerli isaretci
    ///     gostermiyorsa 0. Yeni alanin zemini hazir olmayabilir: cagiran TryReadAt ile sinar.
    /// </summary>
    internal static ulong Current(IntPtr h)
    {
        Holder[] holders;
        lock (Gate) { holders = Live.ToArray(); }

        var votes = new Dictionary<ulong, int>();
        foreach (var x in holders)
        {
            var value = PlayerChain.ReadPointer(h, x.Object + (ulong)x.Back);
            if (PlayerChain.LooksLikePointer(value)) { votes[value] = votes.GetValueOrDefault(value) + 1; }
        }

        return votes.Count == 0 ? 0 : votes.OrderByDescending(v => v.Value).First().Key;
    }

    /// <summary>Konumlanmis (dogrulanmis) bir alani gozlemler: bayat tutucuyu eler, adaylari sinar, gerekirse yenisini cikarir.</summary>
    internal static void Observe(IntPtr h, ulong area)
    {
        if (!PlayerChain.LooksLikePointer(area)) { return; }

        lock (Gate)
        {
            EnsureLoaded();

            // 1. Kalici sanilanlar hala bu alani gosteriyor mu? Gostermiyorsa bayat (yama, oyun yeniden basladi,
            //    ya da hic kalici degildi): elenir.
            var before = Live.Count;
            Live.RemoveAll(x => PlayerChain.ReadPointer(h, x.Object + (ulong)x.Back) != area);
            if (before > 0 && Live.Count < before) { Log?.Invoke($"alan tutucu: {before - Live.Count} bayat tutucu elendi"); }

            // 2. Onceki alandan gelen adaylar bu alani da gosteriyorsa KALICI.
            var confirmed = 0;
            foreach (var p in pending)
            {
                if (PlayerChain.ReadPointer(h, p.Object + (ulong)p.Back) == area && !Live.Contains(p))
                {
                    Live.Add(p);
                    confirmed++;
                }
            }

            pending = new List<Holder>();

            // 3. Hala yok: kayitli tarifler (alan + yuva -> nesne, nesne + geri == alan).
            if (Live.Count == 0)
            {
                foreach (var (slot, back) in recipes)
                {
                    var obj = PlayerChain.ReadPointer(h, area + (ulong)slot);
                    if (!PlayerChain.LooksLikePointer(obj) || obj == area) { continue; }
                    if (PlayerChain.ReadPointer(h, obj + (ulong)back) != area) { continue; }

                    var holder = new Holder(slot, obj, back);
                    if (!Live.Contains(holder)) { Live.Add(holder); }
                }

                if (Live.Count > 0) { Log?.Invoke($"alan tutucu: kayitli tarifle {Live.Count} kalici nesne bulundu"); }
            }

            // 4. Hala yok: bu alandan aday cikar, bir sonraki alan degisiminde sinanacak.
            if (Live.Count == 0)
            {
                pending = Candidates(h, area);
                Log?.Invoke($"alan tutucu: {pending.Count} aday cikarildi, sonraki alan degisiminde sinanacak");
                return;
            }

            if (confirmed > 0)
            {
                recipes = Live.Select(x => (x.Slot, x.Back)).Distinct().Take(MaxRecipes).ToList();
                Save();
                Log?.Invoke($"alan tutucu: {confirmed} aday iki alanda ayni adreste cikti, tarif yazildi " +
                            string.Join(", ", recipes.Select(r => $"alan+0x{r.Slot:X}->+0x{r.Back:X}")));
            }
        }
    }

    /// <summary>Alanin penceresindeki isaretcilerden, alanin kendi blogu disinda olup icinde alanin adresini tutanlar.</summary>
    private static List<Holder> Candidates(IntPtr h, ulong area)
    {
        var result = new List<Holder>();
        var window = new byte[AreaWindow];
        if (!EntityReader.Read(h, area, window)) { return result; }

        var seen = new HashSet<ulong>();
        var buf = new byte[HolderWindow];
        for (var slot = 0; slot + 8 <= window.Length; slot += 8)
        {
            var obj = BitConverter.ToUInt64(window, slot);
            if (!PlayerChain.LooksLikePointer(obj) || obj == area || !seen.Add(obj)) { continue; }
            if (Math.Abs((long)obj - (long)area) < OwnBlock) { continue; }
            if (!EntityReader.Read(h, obj, buf)) { continue; }

            for (var back = 0; back + 8 <= buf.Length; back += 8)
            {
                if (BitConverter.ToUInt64(buf, back) == area) { result.Add(new Holder(slot, obj, back)); }
            }
        }

        return result;
    }

    // ------------------------------------------------------------------ tarif dosyasi

    private static void EnsureLoaded()
    {
        if (loaded) { return; }
        loaded = true;

        try
        {
            if (!File.Exists(ConfigPath)) { return; }

            foreach (var raw in File.ReadAllLines(ConfigPath))
            {
                var line = raw.Trim();
                if (!line.StartsWith("recipe=", StringComparison.OrdinalIgnoreCase)) { continue; }

                var parts = line[7..].Split(':');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out var slot) &&
                    int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var back))
                {
                    recipes.Add((slot, back));
                }
            }
        }
        catch
        {
            // Bozuk dosya uygulamayi durdurmasin; yeniden ogrenilir.
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var lines = new List<string>
            {
                "# Alan tutucu tarifi: alan nesnesi + yuva -> kalici nesne, nesne + geri = su anki alan.",
                "# MapOverlay iki farkli alanda dogrulayarak yazar; yamadan sonra tutmazsa kendiliginden yeniden ogrenir.",
            };
            lines.AddRange(recipes.Select(r => $"recipe={r.Slot:X}:{r.Back:X}"));
            File.WriteAllLines(ConfigPath, lines);
        }
        catch
        {
            // Yazilamazsa sadece bu oturumda ogrenilmis kalir.
        }
    }
}
