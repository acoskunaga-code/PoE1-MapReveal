using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Poe2Map;

/// <summary>
///     Haritayı oyunun kendi görüntüsünün üstüne çizen şeffaf pencere.
///
///     İzdüşüm klasik izometrik: ızgara eksenleri 45 derece döndürülüp dikeyde kamera
///     açısıyla basıklaştırılıyor. Hücre farkından ekran farkına:
///
///         ekranX = (dx - dy) * cos
///         ekranY = -(dx + dy) * sin
///
///     cos ve sin, kamera açısı (38.7 derece) ile ölçekten geliyor. Ölçeği ve merkezi
///     oyunun UI öğesinden okumak yerine ŞİMDİLİK elle ayarlıyoruz: karakterin kendisi
///     ekranda görünen bilinen bir nokta olduğu için, işaret onun üstüne oturana kadar
///     ayarlamak yeterli. Zoom yapınca yeniden ayar gerekir - onu canlı okumak sonraki iş.
/// </summary>
internal sealed partial class OverlayForm : Form
{
    private const int WsExLayered = 0x80000;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;

    // Overlay asla etkinlesmesin: gosterilirken oyunun odagini calmasin, kapanan bir pencerenin ardindan sistem onu
    // "siradaki pencere" diye one getirmesin. Bu olmayinca ayar paneli kapaninca overlay etkin pencere olup kendini
    // gizliyor, oyun odagi geri alinca yeniden gosteriyor ve odak bir sure bu ikisi arasinda gidip geliyordu (titreme).
    private const int WsExNoActivate = 0x08000000;
    private const int GwlExStyle = -20;

    protected override bool ShowWithoutActivation => true;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>
    ///     Kamera acisi. Radar 38.7 kullaniyor ama bizim izdusumumuz onunkiyle ayni
    ///     referansta olmayabilir, o yuzden ayarlanabilir: bu sayi dikey basikligi
    ///     belirliyor ve merkeze uzak noktalardaki kaymanin sebebi genelde bu oluyor.
    /// </summary>
    internal float CameraAngleDeg = 38.7f;

    /// <summary>Dikey ekseni ters cevirir - izdusum aynalanmissa.</summary>
    internal bool FlipY;

    /// <summary>
    ///     Yon tuslariyla kaydirma hizi, kare basina ekran pikseli.
    ///
    ///     Kaydirmayi oyundan OKUMUYORUZ, taklit ediyoruz. Okunacak bir yer yok:
    ///     123 bin UI ogesinin konum/kaydirma alanlari tarandi (temiz bir 2B kaydirma
    ///     cikmadi), harita ogesi kaydirilmisken ortaliyken okunanin birebir aynisini
    ///     veriyor, kamera odagi sandigimiz adresler kaydirilmisken de oyuncunun
    ///     konumunu okuyor, ve dunya-ekran matrisinin kalici bir kopyasi yok. Tek
    ///     gercek sinyal nesne-kamera MESAFELERIydi: kaydirinca ~25'ten ~4200'e
    ///     ciiyor. Yani kaydirma dunya kamerasini tasiyor ve o okunamiyor.
    ///
    ///     O yuzden tuslari kendimiz dinliyoruz ve ayni hizda kaydiriyoruz. Tab
    ///     sifirliyor - oyunun kendi davranisi da bu.
    /// </summary>
    internal float PanSpeed;

    /// <summary>Biriken kaydirma, ekran pikseli - durum satirinda gostermek icin.</summary>
    internal PointF PanScreen => this.panScreen;

    private PointF panScreen;
    private long panClock;
    private IntPtr gameWindow;

    // Oyundan cikmadan ayar yaparken degeri gorebilmek icin kisa sureli yazi.
    private string notice = "";
    private DateTime noticeUntil = DateTime.MinValue;

    /// <summary>
    ///     Overlay uzerinde gorunen kisa bir bilgi yazisi. Varsayilan iki saniye;
    ///     ayarsiz exe "birkac saniye yuru" gibi kalici mesajlar icin daha uzun
    ///     sure veriyor, bos metinle de siliyor.
    /// </summary>
    internal void ShowNotice(string text, double seconds = 2)
    {
        this.notice = text;
        this.noticeUntil = DateTime.UtcNow.AddSeconds(seconds);
        this.Invalidate();
    }

    /// <summary>
    ///     Acikken harita yalnizca oyunun buyuk haritasi (Tab) aciksa ciziliyor.
    ///
    ///     Izdusumun olcegi ve merkezi buyuk harita ogesinden geliyor; Tab kapaliyken
    ///     cizilen sekil dunyanin uzerinde yanlis olcekte yuzer. MapView'de kapali
    ///     (eski davranis korunuyor), ayarsiz exe'de acik.
    /// </summary>
    internal bool HideWhenMapClosed;

    /// <summary>Haritada oyuncunun yerine kirmizi halka + arti (PaintOverlay sonu). Varsayilan kapali.</summary>
    internal bool ShowPlayerMarker;

    private bool mapOpen = true;

    /// <summary>Oyunun buyuk haritasi su an acik mi (son okumaya gore) - gunluk icin.</summary>
    internal bool MapOpen => this.mapOpen;

    /// <summary>
    ///     Ayar paneli acikken harita, oyun on planda olmasa da gorunur kalir. Panel
    ///     on plana gectigi anda overlay gizlenirse simge boyutunu ayarlarken sonucu
    ///     gormek imkansiz olur.
    /// </summary>
    internal bool PanelOpen;

    /// <summary>
    ///     Kapatma tusu (ayarsiz exe, GlobalHotKey): true iken overlay hicbir kosulda gorunmuyor, ayar paneli acik
    ///     olsa bile. Okuma arka planda devam ediyor; tekrar acinca harita beklemeden geliyor.
    /// </summary>
    internal bool Paused;

    /// <summary>
    ///     Oyun tutamacini birakir; bir sonraki okumada yeniden acilir.
    ///     Oyun kapanip yeniden acildiginda eski tutamac olu surece bakiyor.
    /// </summary>
    internal void ResetGameHandle()
    {
        if (this.handle != IntPtr.Zero)
        {
            Native.CloseHandle(this.handle);
            this.handle = IntPtr.Zero;
        }

        this.moduleBase = 0;
        this.MapElement = 0;
        this.Player = null;
    }

    /// <summary>
    ///     Haritayi isaretten BAGIMSIZ kaydirir, hucre cinsinden.
    ///
    ///     Isaret her zaman okudugumuz oyuncu hucresinde duruyor. Sekil zeminle
    ///     ortusurken isaret karakterin uzerinde degilse, okudugumuz konum ile
    ///     karakterin gercek hucresi arasinda sabit bir fark var demektir. Bu iki
    ///     sayi o farki OLCMEYE yariyor: sekli yerine oturtan deger, farkin ta kendisi.
    /// </summary>
    internal float MapOffsetX;
    internal float MapOffsetY;

    /// <summary>
    ///     Haritanin kaydirma miktari, dunya birimi cinsinden (odak eksi oyuncu).
    ///     Yon tuslariyla kaydirma ayri bir alana yazilmiyor, kameranin odagini
    ///     tasiyor; odagin kopyalarindan ortanca alinarak her karede okunuyor.
    /// </summary>
    internal float PanX;
    internal float PanY;

    /// <summary>
    ///     Oyunun harita UI ogesinden canli okunan izdusum. Acikken olcek, aci ve
    ///     merkez elle ayarlanmiyor - oyundan geliyor, dolayisiyla oyuncu zoom
    ///     yaptikca overlay de onunla beraber hareket ediyor.
    /// </summary>
    internal bool UseLive;
    internal float LiveCos;
    internal float LiveSin;
    internal PointF LiveCenter;

    /// <summary>
    ///     Harita merkezinin titremeye karsi olu bolgesi (ekran pikseli). Oyunun harita
    ///     paneli acilirken ya da uzerine gelince ebeveyn zincirindeki bir alan bir sure
    ///     kare kare 1-2 piksel oynayabiliyor; harita da tamamen bu merkeze cakili oldugu
    ///     icin cizgiler git-gel yapiyordu. Bu esigin altindaki oynamalar yok sayiliyor.
    ///
    ///     Gercek hareketi GECIRMIYOR: yurumeyi harita merkezden degil oyuncu konumundan
    ///     takip ediyor (bu deger degismiyor), yon-tusu kaydirmasi da kare basina esigin
    ///     ustunde (5.4 px) ilerledigi icin oldugu gibi gecip gidiyor. Yani gecikme yok.
    /// </summary>
    private const float CenterDeadZone = 2f;
    private PointF centerHeld;
    private bool centerHeldInit;

    private readonly System.Windows.Forms.Timer timer = new();
    private readonly FrameStats stats = new();
    private Bitmap? map;
    private int gridWidth;
    private int gridHeight;

    // Ozellik degil alan: WinForms cozumleyicisi Form uzerindeki ozelliklerden
    // tasarimci serilestirme bilgisi istiyor, bize gereksiz.
    internal PlayerPos? Player;

    /// <summary>Hücre başına ekran pikseli. Zoom değişince ayarlanması gereken sayı.</summary>
    internal float CellScale = 1.6f;

    /// <summary>Karakterin ekrandaki yeri, pencere ortasına göre kayma.</summary>
    internal PointF CenterShift = new(0, 0);

    internal OverlayForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.BackColor = Color.Black;
        this.TransparencyKey = Color.Black;
        this.StartPosition = FormStartPosition.Manual;
        this.DoubleBuffered = true;

        // Asil kare dongusu OverlayForm.Frame.cs'te (ekran yenileme hizinda). WM_TIMER 16 ms'yi 31 ms'ye yuvarladigi icin
        // zamanlayici yalnizca yedek: kare dongusu sustuysa 100 ms'de bir adim atar.
        this.timer.Interval = WatchdogMs;
        this.timer.Tick += (_, _) => this.WatchdogTick();
        this.timer.Start();
    }

    // Kendi kalici tutamaci: konumu ve harita ogesini yuksek hizda kendisi okuyor.
    // Ana pencerenin 400 ms'lik dongusune baglanirsa saniyede 2.5 guncelleme kalir
    // ve oyun 60+ kare cizerken overlay gorunur sekilde geriden gelir.
    private IntPtr handle;
    private ulong moduleBase;
    internal ulong MapElement;

    /// <summary>
    ///     Hizalama kipi. Acikken overlay tiklamalari yakalar (normalde geciriyor).
    ///     Karakterin uzerine tiklayinca, isaretin oldugu yer ile tikladigin yer
    ///     arasindaki fark merkeze ekleniyor - yani goz karari degil, olculerek hizalanir.
    /// </summary>
    /// <summary>
    ///     Hizalama artik tiklamayla degil kisayol tusuyla yapiliyor: saydamlik anahtariyla
    ///     calisan bir pencerede yari saydam bir katman kurulamiyor (piksel ya tam saydam ya
    ///     tam opak), opak katman da oyunu tamamen kapatiyordu.
    /// </summary>
    internal void SetCalibrateMode(bool on)
    {
        this.calibrating = on;
        this.Invalidate();
    }

    private bool calibrating;

    internal bool CalibratingNow => this.calibrating;
    private PointF lastCenter;
    private Point mouseAt;

    /// <summary>Tiklanan nokta, isaretin oldugu nokta, yeni kayma.</summary>
    internal event Action<PointF, PointF, PointF>? Aligned;

    private void SetClickThrough(bool on)
    {
        if (!this.IsHandleCreated) { return; }
        var ex = GetWindowLong(this.Handle, GwlExStyle);
        ex = on ? ex | WsExTransparent : ex & ~WsExTransparent;
        SetWindowLong(this.Handle, GwlExStyle, ex);
    }

    /// <summary>
    ///     Imlecin bulundugu noktayi karakterin yeri kabul edip merkezi oraya tasir.
    ///     Ekran koordinati overlay'in kendi istemci koordinatina cevriliyor.
    /// </summary>
    internal (PointF Cursor, PointF Marker, PointF Shift)? AlignToCursor(Point screenPoint)
    {
        var client = new PointF(screenPoint.X - this.Bounds.Left, screenPoint.Y - this.Bounds.Top);
        var marker = this.lastCenter;
        this.CenterShift = new PointF(
            this.CenterShift.X + (client.X - marker.X),
            this.CenterShift.Y + (client.Y - marker.Y));
        this.Invalidate();
        return (client, marker, this.CenterShift);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!this.calibrating) { return; }

        // Uygulamanin gordugu imlec konumu. Cizilen yesil arti gercek imlecin altinda
        // durmuyorsa koordinat duzlemimiz oyununkiyle ayni degil demektir.
        this.mouseAt = e.Location;
        this.Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!this.calibrating) { return; }

        // Tiklanan nokta karakterin gercek yeri; isaret oradan ne kadar sapmissa
        // merkeze o kadar ekliyoruz.
        var click = new PointF(e.X, e.Y);
        var marker = this.lastCenter;
        var dx = click.X - marker.X;
        var dy = click.Y - marker.Y;
        this.CenterShift = new PointF(this.CenterShift.X + dx, this.CenterShift.Y + dy);
        // Kip acik kalsin: art arda tiklayarak yaklastirmak, tek atista tutturmaktan kolay.
        this.Aligned?.Invoke(click, marker, this.CenterShift);
    }

    private long nextHandleTry;

    private void EnsureHandle()
    {
        if (this.handle != IntPtr.Zero) { return; }

        // FindGame butun sureclerin listesini aliyor (yuzlerce nesne); kare dongusu ekran hizinda (120+ Hz) calisirken oyun
        // yokken her karede cagrilmasin.
        var now = Environment.TickCount64;
        if (now < this.nextHandleTry) { return; }
        this.nextHandleTry = now + GameLookupMs;

        using var game = Native.FindGame();
        if (game == null) { return; }

        this.handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        try { this.moduleBase = (ulong)(game.MainModule?.BaseAddress.ToInt64() ?? 0); }
        catch { this.moduleBase = 0; }
    }

    private const int GameLookupMs = 2000;

    private void ReadLive()
    {
        this.EnsureHandle();
        if (this.handle == IntPtr.Zero) { return; }

        // Tam nitelikli: bu sinifta "Player" adinda bir alan da var, o sinifi golgeliyor.
        var p = global::Poe2Map.Player.TryRead(this.handle, this.moduleBase);
        if (p is not null) { this.Player = p; }

        // Dunya birimi cinsinden kaydirma artik kullanilmiyor: kaydirmayi oyundan
        // okuyamadigimiz icin yon tuslarindan taklit ediyoruz ve o ekran pikseli
        // cinsinden merkeze uygulaniyor (KeyboardPan).
        this.PanX = 0f;
        this.PanY = 0f;

        if (!this.UseLive || this.MapElement == 0)
        {
            this.mapOpen = false;
            return;
        }

        var el = UiFinder.Reread(this.handle, this.MapElement);
        if (el is not { } e)
        {
            this.mapOpen = false;
            return;
        }

        // Sadece yaprak ogenin biti degil, butun ebeveyn zinciri gorunur olmali:
        // Tab ile kapaninca gizlenen sey ust bir kap, yaprağın kendi biti 1 kaliyor.
        var open = UiFinder.ChainVisible(this.handle, this.MapElement);
        if (open != this.mapOpen) { this.stats.MapFlipped(); }
        this.mapOpen = open;

        // Overlay penceresi oyunun istemci alanina birebir oturtuluyor, dolayisiyla
        // bizim ClientSize degerimiz oyunun kendi hesabinda kullandigi olcu.
        var clientW = this.ClientSize.Width;
        var clientH = this.ClientSize.Height;
        if (clientW <= 0 || clientH <= 0) { return; }

        const float ScaleBaseline = 0.187812f;
        var angle = 38.7 * Math.PI / 180;

        // Kosegen ogenin kendi boyutundan DEGIL, oyunun taban cozunurlugundan ve
        // pencere YUKSEKLIGINDEN geliyor. Genislik degistiginde harita olcegi
        // degismiyor - onceki hesabimiz bu yuzden tutmuyordu.
        var diagonal = UiFinder.DiagonalLength(clientH);
        var scale = e.Zoom * ScaleBaseline;
        if (scale <= 0) { return; }

        var mapScale = 240.0 / scale;
        this.LiveCos = (float)(diagonal * Math.Cos(angle) / mapScale);
        this.LiveSin = (float)(diagonal * Math.Sin(angle) / mapScale);

        // Merkez artik tahmin edilmiyor: ogenin ebeveyn zinciri yurunup oyunun
        // kendi konum hesabi tekrarlaniyor, ustune ogenin yarim boyu ve kaydirmalar.
        var computed = UiFinder.MapCenter(this.handle, this.MapElement, clientW, clientH);
        if (computed is not { } c) { return; }

        var pan = this.KeyboardPan();
        var rawCenter = new PointF(c.X + pan.X, c.Y + pan.Y);
        this.stats.Center(rawCenter);
        this.LiveCenter = this.StabilizeCenter(rawCenter);
    }

    /// <summary>
    ///     Merkezi eksen basina tutar: yeni deger eskisinden esikten fazla ayrilmadikca
    ///     eski deger korunuyor (kucuk titremeler yutulur), esigi asinca aynen atlanir
    ///     (gercek harekette gecikme olmaz). Alan degisince ilk buyuk fark zaten esigi
    ///     astigi icin yeni merkeze kendiliginden oturuyor - ayrica sifirlama gerekmiyor.
    /// </summary>
    private PointF StabilizeCenter(PointF raw)
    {
        if (!this.centerHeldInit)
        {
            this.centerHeldInit = true;
            this.centerHeld = raw;
            return raw;
        }

        var x = Math.Abs(raw.X - this.centerHeld.X) > CenterDeadZone ? raw.X : this.centerHeld.X;
        var y = Math.Abs(raw.Y - this.centerHeld.Y) > CenterDeadZone ? raw.Y : this.centerHeld.Y;
        this.centerHeld = new PointF(x, y);
        return this.centerHeld;
    }

    /// <summary>
    ///     Yon tuslarindan biriktirilen kaydirma, ekran pikseli cinsinden.
    ///
    ///     Sadece oyun on plandayken sayiyor - baska pencerede yazarken harita
    ///     kaymasin. Tab sifirliyor, oyunun kendisi de haritayi kapatip acinca
    ///     kaydirmayi sifirliyor.
    /// </summary>
    private PointF KeyboardPan()
    {
        if (this.PanSpeed == 0 || this.gameWindow == IntPtr.Zero ||
            GetForegroundWindow() != this.gameWindow)
        {
            // Saati sifirla: oyuna geri donuldugunde aradaki butun sure tek adimda
            // kaydirma olarak uygulanmasin.
            this.panClock = 0;
            return this.panScreen;
        }

        const int VkTab = 0x09;
        const int VkLeft = 0x25;
        const int VkUp = 0x26;
        const int VkRight = 0x27;
        const int VkDown = 0x28;

        if (Down(VkTab)) { this.panScreen = PointF.Empty; return this.panScreen; }

        // Kaydirma KARE basina degil GECEN SUREYE bagli. Zamanlayici 16 ms istiyor
        // ama Windows'ta granulerlik ~15.6 ms ve ara sira geciktiriyor; kare basina
        // sabit ekleyince her adimda kucuk bir hata birikiyor ve uzun kaydirmada
        // gozle gorulur kaymaya donusuyor.
        //
        // Hizin anlami degismesin diye 60 kare/sn'ye normalize ediyoruz: kutudaki
        // sayi hala "60 fps'de kare basina piksel" demek.
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dt = this.panClock == 0
            ? 0.0
            : (now - this.panClock) / (double)System.Diagnostics.Stopwatch.Frequency;
        this.panClock = now;

        // Uygulama takilip kalirsa tek adimda uc metre kaydirmasin.
        if (dt > 0.2) { dt = 0.2; }

        var step = (float)(this.PanSpeed * dt * 60.0);

        var dx = 0f;
        var dy = 0f;
        if (Down(VkLeft)) { dx += step; }
        if (Down(VkRight)) { dx -= step; }
        if (Down(VkUp)) { dy += step; }
        if (Down(VkDown)) { dy -= step; }

        this.panScreen = new PointF(this.panScreen.X + dx, this.panScreen.Y + dy);
        return this.panScreen;
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    protected override void Dispose(bool disposing)
    {
        if (disposing && this.handle != IntPtr.Zero)
        {
            Native.CloseHandle(this.handle);
            this.handle = IntPtr.Zero;
        }

        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        this.StartPacer();
        var ex = GetWindowLong(this.Handle, GwlExStyle);
        SetWindowLong(this.Handle, GwlExStyle, ex | WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    /// <summary>
    ///     Izgarayı, sıfır olmayan hücreleri yarı saydam çizecek şekilde hazırlar.
    ///     Sıfırlar tamamen saydam kalıyor ki oyunun görüntüsü görünsün.
    /// </summary>
    internal void SetMap(byte[] raw, int bytesPerRow, int width, int height)
    {
        // Ham veriyi saklıyoruz: cizgi/dolgu secimi ya da renk degisince haritayi
        // yeniden istemek yerine buradan tekrar cizeceğiz.
        this.raw = raw;
        this.rawBytesPerRow = bytesPerRow;
        this.gridWidth = width;
        this.gridHeight = height;
        this.wallDistance = null;
        this.RebuildMap();
    }

    /// <summary>
    ///     Cizim kipi, renk ve cizgi kalinligi (hucre). Ozellik degil yontem: WinForms
    ///     cozumleyicisi Form uzerindeki ozelliklerden tasarimci serilestirme bilgisi istiyor.
    /// </summary>
    internal void SetStyle(bool onlyOutline, Color color, int thickness = 1)
    {
        thickness = Math.Clamp(thickness, 1, MaxThickness);
        if (this.outlineOnly == onlyOutline && this.lineColor == color && this.lineThickness == thickness)
        {
            return;
        }

        this.outlineOnly = onlyOutline;
        this.lineColor = color;
        this.lineThickness = thickness;
        this.RebuildMap();
    }

    internal const int MaxThickness = 8;

    private bool outlineOnly = true;
    private Color lineColor = Color.FromArgb(120, 170, 190);
    private int lineThickness = 1;
    private byte[]? raw;
    private int rawBytesPerRow;

    /// <summary>
    ///     Her hucrenin en yakin yurunemez hucreye uzakligi (hucre, MaxThickness'ta
    ///     kirpilmis). Harita basina bir kez hesaplaniyor; kalinlik degisince yeniden
    ///     hesaplamaya gerek yok, sadece bitmap yeniden uretiliyor.
    /// </summary>
    private byte[]? wallDistance;

    /// <summary>
    ///     Izgaradan bitmap uretir.
    ///
    ///     Cizgi kipinde bir hucre, yurunebilir olup en yakin duvara uzakligi kalinlik
    ///     kadar ya da daha az ise ciziliyor. Kalinlik 1 = sadece duvara degen hucreler,
    ///     yani onceki tek hucrelik cizgi. Odalarin ici bos kaliyor. Izgara kenari da
    ///     duvar sayiliyor.
    ///
    ///     Neden kalinlik: zoom 0.5'te bir hucre ekranda bir pikselden kucuk, tek
    ///     hucrelik cizgi en yakin komsu ornekleme yuzunden ince ve kesik gorunuyor.
    /// </summary>
    private void RebuildMap()
    {
        if (this.raw is null || this.gridWidth <= 0 || this.gridHeight <= 0) { return; }

        var width = this.gridWidth;
        var height = this.gridHeight;

        // Bu is arayuz is parcaciginda: surdugu kadar overlay donuyor. Sure gunluge yaziliyor ki ayar degisince "takildi"
        // denince rakam elde olsun.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        this.wallDistance ??= this.ComputeWallDistance();
        var distanceMs = clock.ElapsedMilliseconds;
        var distance = this.wallDistance;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var row = new byte[data.Stride];
            for (var y = 0; y < height; y++)
            {
                Array.Clear(row);
                var line = y * width;
                for (var x = 0; x < width; x++)
                {
                    var d = distance[line + x];
                    if (d == 0) { continue; }

                    byte alpha;
                    if (this.outlineOnly)
                    {
                        if (d > this.lineThickness) { continue; }
                        alpha = 235;
                    }
                    else
                    {
                        alpha = (byte)(this.Cell(x, y) >= 5 ? 90 : 55);
                    }

                    var o = x * 4;
                    row[o] = this.lineColor.B;
                    row[o + 1] = this.lineColor.G;
                    row[o + 2] = this.lineColor.R;
                    row[o + 3] = alpha;
                }

                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        this.map?.Dispose();
        this.map = bmp;
        this.Invalidate();
        CrashLog.Write($"harita bitmap'i uretildi: {width}x{height}, toplam {clock.ElapsedMilliseconds} ms (duvar uzakligi {distanceMs} ms)", null);
    }

    /// <summary>
    ///     Iki gecisli satranc tahtasi uzaklik donusumu: 0 = yurunemez, n = en yakin
    ///     yurunemez hucreye n adim (MaxThickness'ta kirpilir). Dogrusal zamanda;
    ///     her hucre icin pencere taramak kalinlik 3'te hucre basina 49 okuma olurdu.
    /// </summary>
    private byte[] ComputeWallDistance()
    {
        var width = this.gridWidth;
        var height = this.gridHeight;
        var dist = new byte[(long)width * height];
        const byte Far = MaxThickness + 1;

        for (var y = 0; y < height; y++)
        {
            var line = y * width;
            for (var x = 0; x < width; x++)
            {
                dist[line + x] = this.Cell(x, y) == 0 ? (byte)0 : Far;
            }
        }

        // Izgara disi duvar: kenar hucreleri en fazla 1 olabilir.
        byte Get(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? (byte)0 : dist[(y * width) + x];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width) + x;
                if (dist[i] == 0) { continue; }
                var m = Math.Min(Get(x - 1, y), Math.Min(Get(x, y - 1), Math.Min(Get(x - 1, y - 1), Get(x + 1, y - 1))));
                dist[i] = (byte)Math.Min(dist[i], m + 1);
            }
        }

        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                var i = (y * width) + x;
                if (dist[i] == 0) { continue; }
                var m = Math.Min(Get(x + 1, y), Math.Min(Get(x, y + 1), Math.Min(Get(x + 1, y + 1), Get(x - 1, y + 1))));
                dist[i] = (byte)Math.Min(dist[i], m + 1);
            }
        }

        return dist;
    }

    /// <summary>
    ///     Hucre degeri; izgara disi 0 sayiliyor. Tek sayi genislikli izgaralarda
    ///     satir basina bir dolgu yarim bayti var, o yuzden y * satirBayt + x / 2.
    /// </summary>
    private int Cell(int x, int y)
    {
        if (this.raw is null) { return 0; }
        if (x < 0 || y < 0 || x >= this.gridWidth || y >= this.gridHeight) { return 0; }

        var index = ((long)y * this.rawBytesPerRow) + (x / 2);
        if (index < 0 || index >= this.raw.Length) { return 0; }
        return x % 2 == 0 ? this.raw[index] & 0x0F : this.raw[index] >> 4;
    }

    /// <summary>
    ///     Harita yalnizca oyun on plandayken gorunsun. Alt+Tab ile baska bir
    ///     pencereye gecildiginde overlay onunde durmasin.
    ///
    ///     Kalibrasyon kipinde gizlemiyoruz: o sirada MapView on planda oluyor ve
    ///     harita gorunmezse ayar yapilamaz.
    /// </summary>
    private void FollowGameFocus()
    {
        var foreground = GetForegroundWindow();
        var gameActive = this.gameWindow != IntPtr.Zero && foreground == this.gameWindow;

        // Kendi pencerelerimiz (ayar paneli, renk penceresi, tepsi menusu) on plandayken de gorunur kal: oradan
        // oyuna donerken overlay'in kapanip acilmasi bir yanip sonme olarak goruluyordu.
        var ours = !gameActive && foreground != IntPtr.Zero && OwnsWindow(foreground);
        var wanted = !this.Paused && (this.calibrating || this.PanelOpen || gameActive || ours);

        // Gizlemek yavas, gostermek aninda: Alt+Tab animasyonu ya da kisa bir bildirim odagi bir an alirsa
        // overlay her seferinde kapanip acilmasin (bir kapat/ac DWM'nin oyunu yeniden birlestirmesine de yol aciyor).
        var now = Environment.TickCount64;
        if (wanted) { this.unfocusedSince = 0; }
        else if (this.unfocusedSince == 0) { this.unfocusedSince = now; }

        // Kapatma tusu bekletmeden gizliyor.
        var shouldShow = wanted || (!this.Paused && this.Visible && now - this.unfocusedSince < HideAfterMs);

        // Gosterilecekse ya da gorunuyorsa en ustte oldugundan emin ol; gosterilmeden hemen once zorla.
        if (shouldShow) { this.EnsureTopMost(force: !this.Visible); }

        if (this.Visible == shouldShow) { return; }

        this.Visible = shouldShow;
        this.stats.VisibilityFlipped();

        // Gunluk dolmasin: kapat/ac hizla tekrarlanirsa (asil aranan durum) satirlar 300 ms'de bir, gerisi sayac.
        if (now - this.lastVisibilityLog < 300) { return; }
        this.lastVisibilityLog = now;
        CrashLog.Write($"overlay {(shouldShow ? "gosterildi" : "gizlendi")}: onplan {(gameActive ? "oyun" : ours ? "kendi pencere" : "baska pencere")}" +
                       $"  panel {(this.PanelOpen ? "acik" : "kapali")}", null);
    }

    private long lastVisibilityLog;

    private const long HideAfterMs = 200;
    private long unfocusedSince;

    private const int WsExTopMost = 0x8;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoActivate = 0x10;
    private const uint GwHwndPrev = 3;
    private static readonly IntPtr HwndTopMost = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    /// <summary>Penceredeki gercek WS_EX_TOPMOST biti (Form.TopMost ozelligi degil: o, bit silinse de true kalir).</summary>
    internal bool IsTopMostNow => this.IsHandleCreated && (GetWindowLong(this.Handle, GwlExStyle) & WsExTopMost) != 0;

    private long nextTopMostCheck;
    private long lastTopMostLog;

    /// <summary>
    ///     Overlay'i en ustte tutar. 19:13 gozlemi: overlay "acik" ama oyunun ALTINDA ve penceresinde WS_EX_TOPMOST biti yoktu
    ///     (ex=0x080900A0; olmasi gereken ...A8) - cizim yapiyor ama oyunun arkasinda gorunmuyordu, yeniden baslatinca duzeliyordu.
    ///     Bitin neden silindigi bilinmiyor (yalitilmis denemede gizle/goster, panel ac/kapat, Bounds degisikligi silmedi);
    ///     bu yuzden bit YA DA sira bozulursa geri koyuyoruz: ya bit yok ya da oyun penceresi bizim ustumuzde.
    /// </summary>
    private void EnsureTopMost(bool force)
    {
        if (!this.IsHandleCreated) { return; }

        var now = Environment.TickCount64;
        if (!force && now < this.nextTopMostCheck) { return; }
        this.nextTopMostCheck = now + 250;

        var flagMissing = !this.IsTopMostNow;
        var belowGame = !flagMissing && this.gameWindow != IntPtr.Zero && IsBelow(this.Handle, this.gameWindow);
        if (!flagMissing && !belowGame) { return; }

        var ok = SetWindowPos(this.Handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        var error = ok ? 0 : Marshal.GetLastWin32Error();
        var stuck = this.IsTopMostNow;
        this.stats.TopMostRestored();

        // 14.11.2026: iki acilista (13:27, 13:31) bit dakikalarca her kontrolde yine yoktu; overlay oyunun arkasinda kaldi,
        // Fate kutulari gorunmedi. Ikisinde de bir gizle/goster (Alt+Tab) kalici olarak duzeltti. O yuzden bit ust uste
        // birkac kontrolde yoksa ayni seyi kendimiz yapiyoruz (en fazla 10 sn'de bir).
        this.topMostMisses = flagMissing ? this.topMostMisses + 1 : 0;
        if (this.topMostMisses >= TopMostMissesBeforeCycle && this.Visible && now >= this.nextTopMostCycle)
        {
            this.topMostMisses = 0;
            this.nextTopMostCycle = now + 10_000;
            this.Visible = false;
            SetWindowPos(this.Handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            this.Visible = true;
            CrashLog.Write($"overlay en ustte kalmiyordu ({TopMostMissesBeforeCycle} kontrol ust uste), gizle-goster ile yenilendi: " +
                           $"simdi bit {(this.IsTopMostNow ? "var" : "YOK")}", null);
            return;
        }

        if (now - this.lastTopMostLog < 1000) { return; }
        this.lastTopMostLog = now;
        var foreground = GetForegroundWindow();
        CrashLog.Write($"overlay {(flagMissing ? "en ustte degildi (TOPMOST biti yoktu)" : "oyunun altindaydi")}, duzeltildi: " +
                       $"onplan {(foreground == this.gameWindow ? "oyun" : OwnsWindow(foreground) ? "kendi pencere" : "baska pencere")}  " +
                       $"panel {(this.PanelOpen ? "acik" : "kapali")}  gorunur {this.Visible}  " +
                       $"SetWindowPos {(ok ? "tamam" : $"HATA {error}")}, hemen sonra bit {(stuck ? "var" : "yok")}", null);
    }

    private const int TopMostMissesBeforeCycle = 4;
    private int topMostMisses;
    private long nextTopMostCycle;

    /// <summary>window, other'in altinda mi (z sirasinda yukari dogru yururken other bulunur)?</summary>
    private static bool IsBelow(IntPtr window, IntPtr other)
    {
        var current = GetWindow(window, GwHwndPrev);
        for (var i = 0; i < 400 && current != IntPtr.Zero; i++)
        {
            if (current == other) { return true; }
            current = GetWindow(current, GwHwndPrev);
        }

        return false;
    }

    private static bool OwnsWindow(IntPtr window) =>
        GetWindowThreadProcessId(window, out var pid) != 0 && pid == (uint)Environment.ProcessId;

    private long nextGameLookup;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>
    ///     Oyun penceresinin tutamacini bulur ve overlay'i istemci alanina oturtur.
    ///
    ///     Tutamac iki saniyede bir (ya da tutamac olduyse hemen) aranir: Native.FindGame butun surec listesini aliyor, ve bu
    ///     yontem her karede (ekran hizinda) cagriliyordu - saniyede yuzlerce kez surec listesi, 100 ms'ye varan takilmalarin
    ///     (kare tanisinda "takilma") asil nedeni. Kare basina yalnizca iki ucuz pencere sorgusu kaliyor.
    /// </summary>
    private void TrackGameWindow()
    {
        try
        {
            var now = Environment.TickCount64;
            var dead = this.gameWindow != IntPtr.Zero && !IsWindow(this.gameWindow);
            if (dead || now >= this.nextGameLookup)
            {
                this.nextGameLookup = now + GameLookupMs;
                using var game = Native.FindGame();
                if (game != null && game.MainWindowHandle != IntPtr.Zero) { this.gameWindow = game.MainWindowHandle; }
                else if (dead) { this.gameWindow = IntPtr.Zero; }
            }

            var window = this.gameWindow;
            if (window == IntPtr.Zero) { return; }

            // Simge durumuna kucultulmus oyunun istemci alani 0x0 ve (-32000,-32000)'de: boyle bir sinir atamak ne ise yarar ne de
            // Windows sifir boyutu kabul eder (Bounds geri 2x2 doner, her karede "degisti" sayilirdi). Son iyi sinir kalir.
            if (IsIconic(window)) { return; }

            // Pencere degil ISTEMCI alani: pencere dikdortgeni baslik cubugunu ve kenarligi
            // da kapsiyor, oysa oyun sadece istemci alanina ciziyor. Pencereye gore
            // hizalarsak her sey baslik yuksekligi kadar asagi kayiyor.
            if (!GetClientRect(window, out var c)) { return; }
            var origin = new Point(0, 0);
            if (!ClientToScreen(window, ref origin)) { return; }

            var bounds = new Rectangle(origin.X, origin.Y, c.Right - c.Left, c.Bottom - c.Top);
            if (bounds.Width < 64 || bounds.Height < 64) { return; }

            if (this.Bounds != bounds)
            {
                this.Bounds = bounds;
                this.stats.BoundsChanged();
            }
        }
        catch
        {
            // oyun kapandiysa sessizce gec
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            this.profile.Mark("arka plan");
            this.terrainDrawn = false;
            this.PaintOverlay(e);
            this.ClearIfNoTerrain(e);

            // Altar kutulari ve Fate yazilari haritadan bagimsiz: buyuk harita kapaliyken de (OverlayForm.Altars.cs, .Fates.cs).
            this.PaintAltars(e);
            this.PaintFates(e);
            this.PaintUniqueFrames(e);
            this.PaintUniqueAlerts(e);
            this.PaintNotice(e);
            this.profile.Mark("altar/fate/uniq");
        }
        catch (Exception ex)
        {
            // Gecici bir bozuk deger uygulamayi cokertmesin; sonraki karede duzelir. Yine de sessiz degil: yarim cizilen
            // kare titreme gibi gorunur, o yuzden ayni hata saniyede en fazla bir kez gunluge yazilir.
            var now = Environment.TickCount64;
            if (now - this.lastPaintError >= 1000)
            {
                this.lastPaintError = now;
                CrashLog.Write("cizim hatasi", ex);
            }
        }

        this.stats.Paint(started);
    }

    private long lastPaintError;

    private void PaintNotice(PaintEventArgs e)
    {
        if (this.notice.Length == 0 || DateTime.UtcNow > this.noticeUntil) { return; }

        using var font = new Font(this.Font.FontFamily, 16f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(255, 240, 120));
        e.Graphics.DrawString(this.notice, font, brush, 24f, 24f);
    }

    private void PaintOverlay(PaintEventArgs e)
    {
        if (this.map is null || this.Player is not { } p) { return; }
        if (this.HideWhenMapClosed && !this.mapOpen) { return; }

        float cos, sin;
        PointF center;

        if (this.UseLive)
        {
            cos = this.LiveCos;
            sin = this.LiveSin * (this.FlipY ? -1f : 1f);
            center = new PointF(this.LiveCenter.X + this.CenterShift.X, this.LiveCenter.Y + this.CenterShift.Y);
        }
        else
        {
            var angle = this.CameraAngleDeg * Math.PI / 180;
            cos = (float)Math.Cos(angle) * this.CellScale;
            sin = (float)Math.Sin(angle) * this.CellScale * (this.FlipY ? -1f : 1f);
            center = new PointF(
                this.ClientSize.Width / 2f + this.CenterShift.X,
                this.ClientSize.Height / 2f + this.CenterShift.Y);
        }

        // Canli degerler henuz okunmadiysa cos/sin sifir kalir; sifir matrisi GDI+
        // tarafindan reddediliyor ("Parameter is not valid") ve cizim hatasi tum
        // pencereyi kirmizi carpiya cevirir. Hazir olmadan cizmiyoruz.
        if (Math.Abs(cos) < 1e-4f || Math.Abs(sin) < 1e-4f) { return; }

        // Hucre -> ekran: (dx - dy) * cos , -(dx + dy) * sin
        var m = new Matrix(cos, -sin, -cos, -sin, 0, 0);

        // Ekran merkezine gelen sey oyuncu DEGIL, kameranin odagi: oyun buyuk haritayi
        // odaga gore ciziyor ve yon tuslariyla kaydirmak odagi tasiyor. Harita ortaliyken
        // odak oyuncunun ustunde oldugu icin ikisi ayni yere denk geliyor.
        this.lastCenter = center;
        var anchorX = (p.X + this.PanX) / PlayerPos.UnitsPerCell;
        var anchorY = (p.Y + this.PanY) / PlayerPos.UnitsPerCell;
        var pts = new[] { new PointF(anchorX + this.MapOffsetX, anchorY + this.MapOffsetY) };
        m.TransformPoints(pts);
        m.Translate(center.X - pts[0].X, center.Y - pts[0].Y, MatrixOrder.Append);

        // Oyuncunun kendi yeri: kaydirma varken merkezden ayrilmasi gerekiyor.
        var playerPts = new[] { new PointF(p.CellX + this.MapOffsetX, p.CellY + this.MapOffsetY) };
        m.TransformPoints(playerPts);
        var playerAt = playerPts[0];
        this.playerScreen = playerAt;
        this.playerScreenTick = Environment.TickCount64;

        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        // Zemin onbellekten (OverlayForm.TerrainCache.cs): her karede butun haritayi donusumle cizmek pahaliydi.
        this.profile.Mark("hazirlik");
        this.DrawTerrain(e.Graphics, m);
        this.profile.Mark("zemin");

        // Cikis yollari haritanin ustune (OverlayForm.Paths.cs), canavarlar onlarin
        // ustune (OverlayForm.Monsters.cs), oyuncu isareti en uste.
        this.PaintPaths(e, m, p);
        this.profile.Mark("yollar");
        this.PaintWildwood(e, m);
        this.PaintMonsters(e, m);
        this.PaintSpecialMonsters(e, m);
        this.PaintUnhitLegion(e, m);
        this.profile.Mark("canavarlar");
        this.PaintMapIcons(e, m);
        this.profile.Mark("simgeler");
        this.PaintCaches(e, m);
        this.PaintWasting(e, m);
        this.PaintGroundUniques(e, m);
        this.profile.Mark("diger");

            if (this.calibrating)
            {
                using var green = new Pen(Color.FromArgb(255, 90, 255, 120), 2f);
                e.Graphics.DrawLine(green, this.mouseAt.X - 26, this.mouseAt.Y, this.mouseAt.X + 26, this.mouseAt.Y);
                e.Graphics.DrawLine(green, this.mouseAt.X, this.mouseAt.Y - 26, this.mouseAt.X, this.mouseAt.Y + 26);
                e.Graphics.DrawString($"cursor as seen by the app: {this.mouseAt.X}, {this.mouseAt.Y}",
                    this.Font, Brushes.LightGreen, this.mouseAt.X + 12, this.mouseAt.Y + 12);
            }

        // Oyuncu isareti. Merkeze degil oyuncunun kendi yerine ciziliyor: harita
        // kaydirildiginda oyuncu merkezde kalmiyor, oyunun kendi haritasinda da oyle.
        // Varsayilan kapali (kullanici 18.11.2026: "bu isareti kaldirabilirmiyiz"); General sekmesinden acilabiliyor.
        if (!this.ShowPlayerMarker) { return; }

        using var pen = new Pen(Color.FromArgb(220, 255, 90, 60), 2f);
        e.Graphics.DrawEllipse(pen, playerAt.X - 8, playerAt.Y - 8, 16, 16);
        e.Graphics.DrawLine(pen, playerAt.X - 14, playerAt.Y, playerAt.X - 4, playerAt.Y);
        e.Graphics.DrawLine(pen, playerAt.X + 4, playerAt.Y, playerAt.X + 14, playerAt.Y);
        e.Graphics.DrawLine(pen, playerAt.X, playerAt.Y - 14, playerAt.X, playerAt.Y - 4);
        e.Graphics.DrawLine(pen, playerAt.X, playerAt.Y + 4, playerAt.X, playerAt.Y + 14);
    }
}
