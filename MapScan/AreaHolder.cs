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

    /// <summary>Bu oturumda alan degisimini takip etmeyen (bayat cikan) bir tarif bu kadar olunca siliniyor.</summary>
    private const int MissesToDrop = 2;

    private readonly record struct Holder(int Slot, ulong Object, int Back);

    private static readonly object Gate = new();
    private static readonly List<Holder> Live = new();
    private static List<Holder> pending = new();
    private static ulong pendingArea;
    private static List<(int Slot, int Back)> recipes = new();
    private static bool loaded;

    // 15.11.2026, bir kullanicinin gunlugu: karakter secim ekranina donunce dogru tarif (2C0:218, 580:A0, 880:28) o
    // ekranda tutmadi, oradan cikan TEK yanlis aday (300:10, alanla birlikte yeniden olusan bir nesne) iki yuklemede
    // ayni adreste kalinca "kalici" sayildi ve dosyadaki dogru tarifin USTUNE yazildi. Sonra her alan degisiminde o
    // tutucu bayat cikti, tarifle yeniden bulundu, aday hic cikarilmadi: 104 yuklemenin 81'i 8 sn bekleme + tarama.
    // Simdi: tarifler birlestiriliyor (yeni + eski), bir alan degisimini gercekten takip eden tutucu "kanitli" sayiliyor,
    // kanitli tutucu yokken aday cikarmaya devam ediliyor, takip etmeyen tarif iki kez bayat cikinca siliniyor.
    private static readonly HashSet<Holder> Proven = new();
    private static readonly Dictionary<(int Slot, int Back), int> Misses = new();
    private static readonly HashSet<(int Slot, int Back)> ProvenRecipes = new();
    private static ulong lastArea;

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

    /// <summary>
    ///     Henuz dogrulanmamis adaylarin gosterdigi YENI alan: adaylarin cikarildigi alandan farkli, en cok oyu alan isaretci;
    ///     yoksa 0. Tarif henuz ogrenilmemisken (yeni kurulum, yamadan sonra) ILK alan degisiminin de taramasiz olmasi
    ///     icin: eskiden bu degisim 8 sn bekleyip 4-5 sn tariyordu. Sonuc yalnizca aday; cagiran LocalPlayer.FromArea
    ///     ile sinar (zemin + oyuncu), yanlis bir aday haritayi bozamaz.
    /// </summary>
    internal static ulong PendingCurrent(IntPtr h)
    {
        Holder[] candidates;
        ulong from;
        lock (Gate)
        {
            candidates = pending.ToArray();
            from = pendingArea;
        }

        var votes = new Dictionary<ulong, int>();
        foreach (var x in candidates)
        {
            var value = PlayerChain.ReadPointer(h, x.Object + (ulong)x.Back);
            if (value != from && PlayerChain.LooksLikePointer(value)) { votes[value] = votes.GetValueOrDefault(value) + 1; }
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
            var changed = lastArea != 0 && area != lastArea;
            lastArea = area;

            // 1. Kalici sanilanlar hala bu alani gosteriyor mu? Gostermiyorsa bayat (yama, oyun yeniden basladi,
            //    ya da hic kalici degildi): elenir. Alan degistiyse takip edenler kanitli, etmeyenlerin tarifi bir ceza
            //    aliyor; bu oturumda hic takip etmemis bir tarif MissesToDrop cezada siliniyor.
            var stale = Live.Where(x => PlayerChain.ReadPointer(h, x.Object + (ulong)x.Back) != area).ToList();
            var dropped = new List<(int Slot, int Back)>();
            foreach (var x in Live.Except(stale).Where(_ => changed))
            {
                Proven.Add(x);
                ProvenRecipes.Add((x.Slot, x.Back));
            }

            foreach (var x in stale)
            {
                Live.Remove(x);
                Proven.Remove(x);
                var key = (x.Slot, x.Back);
                if (!changed || ProvenRecipes.Contains(key)) { continue; }

                Misses[key] = Misses.GetValueOrDefault(key) + 1;
                if (Misses[key] >= MissesToDrop && recipes.Remove(key)) { dropped.Add(key); }
            }

            if (stale.Count > 0) { Log?.Invoke($"alan tutucu: {stale.Count} bayat tutucu elendi"); }
            if (dropped.Count > 0)
            {
                Save();
                Log?.Invoke("alan tutucu: alan degisimini takip etmeyen tarif silindi " +
                            string.Join(", ", dropped.Select(r => $"alan+0x{r.Slot:X}->+0x{r.Back:X}")));
            }

            // 2. Onceki alandan gelen adaylar bu alani da gosteriyorsa KALICI (bir alan degisimini takip ettiler: kanitli).
            var confirmed = 0;
            foreach (var p in pending)
            {
                if (PlayerChain.ReadPointer(h, p.Object + (ulong)p.Back) == area && !Live.Contains(p))
                {
                    Live.Add(p);
                    Proven.Add(p);
                    ProvenRecipes.Add((p.Slot, p.Back));
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

            // 4. Kanitli tutucu yok (hic yok, ya da sadece tarifle bulunmus, henuz bir alan degisimini takip etmemis olan):
            //    bu alandan aday cikar, bir sonraki alan degisiminde sinanacak. Eskiden yalnizca hic tutucu yokken
            //    cikariliyordu; yanlis bir tarif her seferinde "bulununca" ogrenme sonsuza dek duruyordu.
            if (!Live.Any(Proven.Contains))
            {
                pending = Candidates(h, area).Where(c => !Live.Contains(c)).ToList();
                pendingArea = area;
                Log?.Invoke($"alan tutucu: {pending.Count} aday cikarildi, sonraki alan degisiminde sinanacak");
                if (Live.Count == 0) { return; }
            }

            if (confirmed > 0)
            {
                // Yeni tarifler one, eskiler korunarak (bir alanda tutmayan dogru tarif kaybolmasin).
                recipes = Live.Where(Proven.Contains).Select(x => (x.Slot, x.Back))
                    .Concat(recipes).Distinct().Take(MaxRecipes).ToList();
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
