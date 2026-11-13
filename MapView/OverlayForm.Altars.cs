namespace Poe2Map;

/// <summary>
///     Eldritch altarlari: oyunun altar ustunde gosterdigi iki secenekten "Player gains:" ile baslayanin kutusu
///     kalin yesil, kullanicinin yazdigi yazilardan biri gecenin kutusu (kim kazanirsa kazansin: Player, Map boss,
///     Eldritch Minions) altin rengi cerceveyle isaretleniyor; ikisi birden ise altin disarida, yesil iceride.
///     Kutularin metni ve yeri oyunun kendi etiketlerinden (AltarLabels): cerceve oyunun kutusunun tam ustune
///     oturuyor. Buyuk harita kapaliyken de ciziliyor.
///
///     Iki hiz: etiketler saniyede dort kez taraniyor (RefreshMonsters ile ayni zamanlayici), kutunun ekrandaki
///     yeri her karede okunuyor (kamera kaydikca etiket de kayiyor). Etiket kabini ilk kez bulmak bir agac
///     taramasi; arka planda, sadece alanda altar varken ve en fazla bes saniyede bir yapiliyor.
/// </summary>
internal sealed partial class OverlayForm
{
    private const string AltarTarget = "Player";

    private static readonly Pen AltarFrameEdge = new(Color.FromArgb(20, 20, 20), 1f);

    // Renkleri kullanici seciyor (ayar penceresi, Altars sekmesi). Kalem nesnesi butun olarak degistiriliyor, eskisi
    // Dispose edilmiyor: o anda cizimde kullaniliyor olabilir, cop toplayici kapatiyor.
    private Pen altarPlayerFrame = new(Color.FromArgb(40, 255, 110), 3f);
    private Pen altarTextFrame = new(Color.FromArgb(255, 200, 40), 3f);

    private sealed record AltarMark(AltarLabels.Option Option, bool Player, bool Text);

    private bool altarPlayer = true;
    private string[] altarPhrases = Array.Empty<string>();
    private ulong altarContainer;

    // Kabin bulundugu andaki buyuk harita ogesi ve arayuz koku. Oyun arayuzu yeniden kurabiliyor (12.11.2026: siginaga
    // gidip haritaya donunce harita ogesinin adresi degisti); eski kap bellekte kendi adresini tutmaya devam ettigi icin
    // gecerli gorunuyor ama artik ekrandaki etiketleri tasimiyor. Harita ogesi ya da kok degisince kap atiliyor.
    private ulong altarContainerElement;
    private ulong altarContainerRoot;
    private long altarContainerSince;
    private long altarLabelsSeenAt;

    // Kabin kokten yolu (AltarLabels.PathFile). Yoldan gelen kap metinle dogrulanmadi sayiliyor (altarFromPath).
    private int[]? altarPath;
    private bool altarPathLoaded;
    private bool altarFromPath;
    private bool altarPathFailed;
    private AltarMark[] altarOptions = Array.Empty<AltarMark>();
    private long altarsSeenAt;
    private long nextAltarSearch;
    private int altarSearching;

    private bool AltarWanted => this.altarPlayer || this.altarPhrases.Length > 0;

    /// <summary>
    ///     Hangi altar seceneklerinin cercevelenecegi (ayar penceresi): "Player gains" ve aranan yazilar. Yazilar
    ///     karsilastirma bicimine getirilip kopyalaniyor; 3 harften kisa kalanlar (ornek: sadece bir sayi) atiliyor.
    /// </summary>
    internal void SetAltarHighlight(bool player, IEnumerable<string> phrases, Color playerColor, Color textColor)
    {
        if (this.altarPlayerFrame.Color.ToArgb() != KeepOffKey(playerColor).ToArgb()) { this.altarPlayerFrame = new Pen(KeepOffKey(playerColor), 3f); }
        if (this.altarTextFrame.Color.ToArgb() != KeepOffKey(textColor).ToArgb()) { this.altarTextFrame = new Pen(KeepOffKey(textColor), 3f); }

        this.altarPlayer = player;
        this.altarPhrases = phrases.Select(AltarLabels.Normalize).Where(p => p.Length >= 3).Distinct().ToArray();
        if (!this.AltarWanted) { this.altarOptions = Array.Empty<AltarMark>(); }
    }

    /// <summary>Saf siyah pencerenin saydamlik anahtari: siyah secilirse cerceve gorunmez olurdu, (1, 1, 1)'e cekiliyor.</summary>
    private static Color KeepOffKey(Color c) => c.R == 0 && c.G == 0 && c.B == 0 ? Color.FromArgb(255, 1, 1, 1) : Color.FromArgb(255, c);

    /// <summary>Secenek cercevelenecek mi; oyleyse hangi renk(ler)le. Yoksa null.</summary>
    private AltarMark? Mark(AltarLabels.Option option)
    {
        var player = this.altarPlayer && option.Target == AltarTarget;
        var text = false;
        if (this.altarPhrases.Length > 0)
        {
            var normal = AltarLabels.Normalize(option.Text);
            text = this.altarPhrases.Any(p => normal.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        return player || text ? new AltarMark(option, player, text) : null;
    }

    /// <summary>Varlik gezintisinde cagriliyor: alanda bir Eldritch altari var mi (aramayi bosuna yapmamak icin).</summary>
    private void NoteAltar(EntityReader.EntityInfo info)
    {
        if (info.Path.Contains("/PrimordialBosses/", StringComparison.Ordinal) &&
            info.Path.EndsWith("Altar", StringComparison.Ordinal))
        {
            this.altarsSeenAt = Environment.TickCount64;
        }
    }

    /// <summary>Arka plan: etiketleri tara; kap bilinmiyorsa ve alanda altar varsa kabi ara. Saniyede dort kez.</summary>
    private void RefreshAltars(IntPtr h, bool entitiesWalked)
    {
        if (!this.AltarWanted || h == IntPtr.Zero || this.MapElement == 0)
        {
            this.altarOptions = Array.Empty<AltarMark>();
            return;
        }

        var now = Environment.TickCount64;
        var recent = now - this.altarsSeenAt < 10_000;

        // Kap yoksa once kayitli yoldan (AltarLabels.PathFile): aninda, ekranda altar etiketi olmasa da.
        if (this.altarContainer == 0) { this.ResolveAltarContainer(h, now); }

        if (this.altarContainer != 0)
        {
            var reason = this.StaleContainer(h);
            if (reason is null && AltarLabels.Scan(h, this.altarContainer) is { } options)
            {
                if (options.Count > 0)
                {
                    // Yoldan gelen kapta altar etiketi goruldu: yol dogru, artik dogrulanmis sayiliyor.
                    this.altarLabelsSeenAt = now;
                    this.altarFromPath = false;
                }

                this.altarOptions = options.Select(this.Mark).OfType<AltarMark>().ToArray();

                // Yoldan gelen kap yanlissa (oyun arayuzu degistirmis): altar varken uzun sure etiket cikmazsa metinle ara.
                var quiet = now - Math.Max(this.altarLabelsSeenAt, this.altarContainerSince) > 8_000;
                if (!(this.altarFromPath && recent && quiet)) { return; }

                reason = "kayitli yoldan gelen kapta 8 sn altar etiketi yok";
            }

            CrashLog.Write($"altar etiket kabi birakildi ({this.altarContainer:X}): {reason ?? "kendi adresini tutmuyor"}, yeniden aranacak", null);
            this.altarContainer = 0;
            this.altarPathFailed = this.altarFromPath;
            this.nextAltarSearch = 0;
        }

        this.altarOptions = Array.Empty<AltarMark>();

        // Varlik gezintisi yapildiysa sadece altar gorulmusse ara; yapilmadiysa (canavar ve simgeler kapali) seyrek ara.
        if ((entitiesWalked && !recent) || now < this.nextAltarSearch) { return; }
        if (Interlocked.Exchange(ref this.altarSearching, 1) == 1) { return; }

        this.nextAltarSearch = now + (recent ? 5_000 : 20_000);
        var element = this.MapElement;
        Task.Run(() =>
        {
            try
            {
                var started = Environment.TickCount64;
                var root = AltarLabels.RootOf(h, element);
                var container = AltarLabels.FindContainer(h, root);
                if (container != 0)
                {
                    this.altarContainerElement = element;
                    this.altarContainerRoot = root;
                    this.altarContainerSince = Environment.TickCount64;
                    this.altarFromPath = false;
                    this.altarPathFailed = false;
                    this.altarContainer = container;

                    // Yolu kaydet: bundan sonra (ve sonraki acilislarda) kap aramasiz bulunur.
                    var path = AltarLabels.PathOf(h, root, container);
                    if (path is not null && (this.altarPath is null || !path.SequenceEqual(this.altarPath)))
                    {
                        this.altarPath = path;
                        AltarLabels.SavePath(path);
                    }

                    CrashLog.Write($"altar etiket kabi bulundu: {container:X} yol {(path is null ? "?" : string.Join('/', path))} " +
                                   $"({Environment.TickCount64 - started} ms)", null);
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("altar etiket aramasi", ex);
            }
            finally
            {
                Interlocked.Exchange(ref this.altarSearching, 0);
            }
        });
    }

    /// <summary>
    ///     Kayitli yoldan kabi bulur (dosyadan bir kez okunuyor). Yol bu oturumda bir kez yanlis ciktiysa metinli
    ///     arama yeni yolu yazana kadar kullanilmiyor.
    /// </summary>
    private void ResolveAltarContainer(IntPtr h, long now)
    {
        if (!this.altarPathLoaded)
        {
            this.altarPathLoaded = true;
            this.altarPath = AltarLabels.LoadPath();
        }

        if (this.altarPath is not { } path || this.altarPathFailed) { return; }

        var element = this.MapElement;
        var root = AltarLabels.RootOf(h, element);
        var container = AltarLabels.Resolve(h, root, path);
        if (container == 0) { return; }

        this.altarContainerElement = element;
        this.altarContainerRoot = root;
        this.altarContainerSince = now;
        this.altarFromPath = true;
        this.altarContainer = container;
    }

    /// <summary>
    ///     Kap hala ekrandaki etiketleri mi tasiyor; tasimiyorsa nedeni. Iki sinav: harita ogesi ayni mi, kap hala
    ///     ayni koke bagli mi. ("Altar var ama etiket yok" sinavi bilerek yok: kullanilmis altarin etiketi kalkiyor,
    ///     varlik kaliyor; o sinav harita boyunca bes saniyede bir butun agaci taratirdi.)
    /// </summary>
    private string? StaleContainer(IntPtr h)
    {
        if (this.MapElement != this.altarContainerElement) { return "harita ogesi degisti"; }
        return AltarLabels.RootOf(h, this.altarContainer) != this.altarContainerRoot ? "arayuz koku degisti" : null;
    }

    /// <summary>Her karede: isaretlenen kutularin etrafina cerceve. OnPaint'ten, harita kapaliyken de.</summary>
    private void PaintAltars(PaintEventArgs e)
    {
        var options = this.altarOptions;
        var h = this.handle;
        if (options.Length == 0 || h == IntPtr.Zero) { return; }

        var width = this.ClientSize.Width;
        var height = this.ClientSize.Height;

        foreach (var mark in options)
        {
            var element = mark.Option.Element;
            if (UiFinder.Reread(h, element) is not { } el || !UiFinder.ChainVisible(h, element)) { continue; }
            if (UiFinder.ElementPosition(h, element, width, height) is not { } at) { continue; }

            var (sx, sy) = UiFinder.ScaleValue(el.ScaleIndex, el.LocalScale, width, height, 0);
            var box = new RectangleF(at.X, at.Y, el.SizeX * sx, el.SizeY * sy);
            if (box.Width < 4 || box.Height < 4 || box.Right < 0 || box.Bottom < 0 || box.Left > width || box.Top > height) { continue; }

            box.Inflate(3, 3);
            if (mark.Player)
            {
                e.Graphics.DrawRectangle(this.altarPlayerFrame, box.X, box.Y, box.Width, box.Height);
                box.Inflate(3, 3);
            }

            if (mark.Text)
            {
                e.Graphics.DrawRectangle(this.altarTextFrame, box.X, box.Y, box.Width, box.Height);
                box.Inflate(3, 3);
            }

            box.Inflate(-0.5f, -0.5f);
            e.Graphics.DrawRectangle(AltarFrameEdge, box.X, box.Y, box.Width, box.Height);
        }
    }
}
