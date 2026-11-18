namespace Poe2Map;

/// <summary>
///     Ayarsiz haritanin denetleyicisi. Pencere yok; saniyede bir su soruları soruyor:
///
///       - oyun acik mi, ayni surec mi (yeniden baslatildiysa her sey sifirlanir)
///       - harita yuklu mu (degilse oyuncuyu sabitleyip zemini yukle)
///       - alan degisti mi (degistiyse yeniden yukle)
///       - buyuk harita ogesi biliniyor mu (bilinmiyorsa bul)
///
///     Pahali isler (bellek taramalari, yuruyus beklemesi) arka planda yapiliyor;
///     overlay her zaman arayuz is parcaciginda guncelleniyor.
/// </summary>
internal sealed partial class OverlayContext : ApplicationContext
{
    // PoE2'de kaydirma bellekten okunamadigi icin yon tuslarindan taklit ediliyordu (5.4 piksel/kare). PoE1'de
    // gercek kaydirma okunuyor (UiFinder.MapCenter), taklit onun ustune biniyor ve haritayi geride birakiyordu: KAPALI.
    private const float PanSpeed = 0f;

    private readonly OverlaySettings settings = OverlaySettings.Load();

    // Simge adlari ve sayfasi: hem ayar penceresinin Icons sekmesi hem haritadaki simgeler bunu kullaniyor.
    // Dagitilan surumde yok; ilk acilista indiriliyor (OverlayContext.Icons.cs, IconDownload.cs).
    private IconCatalog? iconCatalog = IconCatalog.Load();

    // Kisayollar (General sekmesinde secilen tuslar): overlay'i acip kapatan ve cikis yollarini gizleyen.
    private readonly GlobalHotKey toggleKey = new(GlobalHotKey.ToggleId);
    private readonly GlobalHotKey pathKey = new(GlobalHotKey.PathsId);
    private SettingsForm? settingsForm;
    private string lastState = "";
    private DateTime nextStateLog = DateTime.MinValue;

    /// <summary>
    ///     Iki okuma arasinda bu kadar yol alinamaz: teleport, waypoint ya da alan
    ///     degisimi demek. Hareket becerileriyle bir saniyede alinabilecek yolun
    ///     ustunde tutuldu.
    /// </summary>
    private const float AreaJump = 2500f;

    /// <summary>
    ///     Oyuncunun konumu arka arkaya bu kadar okumada yuklu zeminde yurunebilir
    ///     degilse alan degismis sayiliyor. Atlama testi kacirirsa bu yakalar: yeni
    ///     alanin dogma noktasi eski konuma yakin olabilir, ama eski izgarada
    ///     yurunebilir bir hucreye denk gelmesi pek olasi degil.
    /// </summary>
    private const int StrikesForAreaChange = 3;

    private readonly OverlayForm overlay = new();
    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer timer = new();

    private IntPtr handle;
    private ulong moduleBase;
    private int gamePid;

    private TerrainFinder.Terrain? terrain;
    private PlayerPos? lastPos;
    private int strikes;
    private int failures;
    private DateTime nextGameCheck = DateTime.MinValue;

    /// <summary>
    ///     Bilinen alan yapilari. Eski olsalar da + 0x5D0'da guncel oyuncuyu gosteriyorlar;
    ///     oyuncu varligi degisse bile yeni alana taramasiz buradan ulasiliyor.
    /// </summary>
    private readonly List<ulong> knownAreas = new();
    private bool busy;
    private bool exiting;

    /// <summary>Isaretlenince overlay normal kapanir: [Threading.EventWaitHandle]::OpenExisting('Local\Poe1Map.MapOverlay.Exit').Set().</summary>
    private const string ExitSignalName = @"Local\Poe1Map.MapOverlay.Exit";

    private readonly EventWaitHandle exitSignal;
    private DateTime nextLocate = DateTime.MinValue;
    private DateTime nextElementSearch = DateTime.MinValue;

    internal OverlayContext()
    {
        this.overlay.UseLive = true;
        this.overlay.HideWhenMapClosed = true;
        this.overlay.PanSpeed = PanSpeed;
        this.toggleKey.Pressed += this.ToggleOverlay;
        this.pathKey.Pressed += this.TogglePaths;
        this.ApplySettings();
        this.overlay.Show();
        this.FetchIconsIfMissing();

        // Oyunda gorulen cikis adlari ayar listesine (Paths sekmesi); arka plandan geliyor, arayuz is parcacigina aktar.
        this.overlay.ExitNamesSeen = names => this.Post(() => this.NoteExitNames(names));

        // Overlay penceresi kapanirsa (oturum kapanisi) normal cikis olsun: tepsi simgesi kalmasin.
        this.overlay.FormClosed += (_, _) =>
        {
            if (!this.exiting) { this.ExitThread(); }
        };

        // taskkill (WM_CLOSE) overlay'a ulasmiyor (14.11.2026: "termination signal" gitti, surec kapanmadi). Temiz kapatma
        // icin adli olay: isaretlenince normal cikis. Yeniden baslatirken zorla oldurmek tepside hayalet simge birakiyordu.
        this.exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
        ThreadPool.RegisterWaitForSingleObject(
            this.exitSignal, (_, _) => this.overlay.BeginInvoke(this.ExitThread), null, Timeout.Infinite, executeOnlyOnce: true);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => this.OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => this.ExitThread());

        this.tray.Icon = SystemIcons.Application;
        this.tray.Text = "PoE1 Map";
        this.tray.ContextMenuStrip = menu;
        this.tray.DoubleClick += (_, _) => this.OpenSettings();
        this.tray.Visible = true;

        // 250 ms: alan degisimi artik tek bir isaretci okumasiyla algilaniyor, sik bakmak ucuz.
        this.timer.Interval = 250;
        this.timer.Tick += (_, _) => this.Tick();
        this.timer.Start();
        this.OpenSettingsFromArgs();
    }

    private void ApplySettings()
    {
        this.overlay.SetStyle(true, this.settings.LineColor, this.settings.Thickness);
        this.overlay.SetMonsterStyle(this.settings.MonsterVisible, this.settings.MonsterSize);
        this.overlay.SetMapIcons(
            this.settings.IconSizes,
            this.iconCatalog is { HasImages: true } catalog ? catalog.Sprite : null,
            this.settings.IconNames);
        this.overlay.SetAltarHighlight(
            this.settings.AltarPlayer, this.settings.AltarPhrases, this.settings.AltarPlayerColor, this.settings.AltarTextColor);
        this.overlay.SetFateLabels(this.settings.FateRewards, this.settings.FatePhrases, this.settings.FateTextColor);
        this.overlay.SetFatePlan(this.settings.FatePlan, this.settings.FateRanking);
        this.overlay.SetHiddenPaths(this.settings.HiddenPaths);
        this.overlay.SetWildwood(this.settings.Wildwood);
        this.overlay.SetDjinnCaches(this.settings.DjinnCaches);
        using (var uniqueFont = this.settings.CreateUniqueFont())
        {
            this.overlay.SetGroundUniques(this.settings.ShownUniques, this.settings.PathUniques, uniqueFont, this.settings.UniqueColor);
            this.overlay.SetUniqueAlerts(this.settings.AlertUniques, this.settings.UniqueAlertSize);
            this.overlay.SetUniqueFrames(this.settings.FrameUniques);
        }

        RegisterKey(this.toggleKey, this.settings.ToggleKey, "acma/kapama tusu");
        RegisterKey(this.pathKey, this.settings.PathKey, "yol tusu");
    }

    /// <summary>Tus degistiyse yeniden kaydeder. Ayni tus iki ise secilirse ikincisi kaydedilemez (gunlukte gorunur).</summary>
    private static void RegisterKey(GlobalHotKey hotKey, Keys keys, string what)
    {
        if (hotKey.Current == keys) { return; }

        var ok = hotKey.Register(keys);
        CrashLog.Write($"{what} {GlobalHotKey.Describe(keys)}: " +
                       (ok ? "kaydedildi" : "KAYDEDILEMEDI (baska bir program ya da diger kisayol kullaniyor olabilir)"), null);
    }

    /// <summary>Yol tusu: cikis yollarini gizler / geri getirir. Overlay kapali degilse kisa bir yazi gosteriyor.</summary>
    private void TogglePaths()
    {
        this.overlay.HidePaths = !this.overlay.HidePaths;
        this.overlay.ShowNotice(this.overlay.HidePaths ? "Exit paths: OFF" : "Exit paths: ON", 1.5);
        this.overlay.Invalidate();
        CrashLog.Write($"cikis yollari {(this.overlay.HidePaths ? "gizlendi" : "gosteriliyor")} (tus {GlobalHotKey.Describe(this.settings.PathKey)})", null);
    }

    /// <summary>Kisayol: overlay'i tamamen gizler / geri getirir. Acilinca kisa bir yazi gosteriyor.</summary>
    private void ToggleOverlay()
    {
        this.overlay.Paused = !this.overlay.Paused;
        this.tray.Text = this.overlay.Paused ? "PoE1 Map (off)" : "PoE1 Map";
        if (!this.overlay.Paused) { this.overlay.ShowNotice("PoE1 Map: ON", 1.5); }

        CrashLog.Write($"overlay {(this.overlay.Paused ? "kapatildi" : "acildi")} (tus {GlobalHotKey.Describe(this.settings.ToggleKey)})", null);
    }
    private void OpenSettings(string? tab = null)
    {
        if (this.settingsForm is { IsDisposed: false })
        {
            this.settingsForm.Activate();
            return;
        }

        this.settingsForm = new SettingsForm(this.settings, this.ApplySettings, this.iconCatalog);

        // Panel on plana gecince oyun arkada kaliyor; harita gizlenirse yaptigin
        // ayarin sonucu gorulmez.
        this.overlay.PanelOpen = true;
        this.settingsForm.FormClosed += (_, _) => this.overlay.PanelOpen = false;
        this.settingsForm.Show();
        if (tab is not null) { this.settingsForm.SelectTab(tab); }
    }

    /// <summary>"--settings [sekme]" ile baslatilinca ayar penceresi hemen acilir (gelistirme: sekmeyi elle acmadan denemek).</summary>
    private void OpenSettingsFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.FindIndex(args, a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase));
        if (at > 0) { this.OpenSettings(at + 1 < args.Length ? args[at + 1] : null); }
    }

    /// <summary>
    ///     Durumu gunluge yazar, sadece degistiginde ve en fazla bes saniyede bir.
    ///     Pencere olmadigi icin "harita takip etmiyor" gibi bir sorunda uygulamanin
    ///     ne gordugunu gormenin tek yolu bu.
    /// </summary>
    private void LogState(string note = "")
    {
        var overlayPos = this.overlay.Player is { } op ? $"({op.X:F0}, {op.Y:F0})" : "yok";
        var area = this.terrain is { } t ? $"{t.GridX}x{t.GridY}" : "yok";

        // Shift merkeze eklenmiyor ama degeri gunlukte duruyor: bir gun yeniden
        // hizalama bozulursa ilk bakilacak yer.
        var shift = this.handle != IntPtr.Zero && UiFinder.Reread(this.handle, this.overlay.MapElement) is { } el
            ? $"({el.ShiftX:F0}, {el.ShiftY:F0})"
            : "-";
        var source = LocalPlayer.Current is not null ? "varlik" : $"kopya {LivePosition.Count}";

        var state = $"kaynak {source}  overlay konumu {overlayPos}  alan {area}  " +
                    $"oge 0x{this.overlay.MapElement:X}  shift {shift}  tab {(this.overlay.MapOpen ? "acik" : "kapali")}  " +
                    $"gorunur {this.overlay.Visible}  ustte {this.overlay.IsTopMostNow}";

        if (note.Length > 0)
        {
            CrashLog.Write(note + "  |  " + state, null);
            this.lastState = state;
            return;
        }

        if (state == this.lastState || DateTime.UtcNow < this.nextStateLog) { return; }
        this.lastState = state;
        this.nextStateLog = DateTime.UtcNow.AddSeconds(5);
        CrashLog.Write("durum  |  " + state, null);
    }

    protected override void ExitThreadCore()
    {
        if (this.exiting) { return; }

        this.exiting = true;
        this.timer.Stop();

        // Kapatirken paneli kendimiz kapatiyoruz ki ayarlar diske yazilsin: mesaj
        // dongusu bitince FormClosed artik calismiyor.
        if (this.settingsForm is { IsDisposed: false }) { this.settingsForm.Close(); }

        // Ayar penceresi hic acilmadiysa da gorulen cikis adlari kaybolmasin.
        this.settings.Save();

        this.tray.Visible = false;
        this.tray.Dispose();
        this.overlay.Close();
        this.toggleKey.Dispose();
        this.pathKey.Dispose();
        this.iconCatalog?.Dispose();
        this.exitSignal.Dispose();
        this.CloseOwnHandle();
        base.ExitThreadCore();
    }

    private void Tick()
    {
        if (this.busy) { return; }

        // Surec listesini dolasmak pahali; tik 250 ms'ye inince her tikte yapilmasin.
        if (DateTime.UtcNow >= this.nextGameCheck)
        {
            this.nextGameCheck = DateTime.UtcNow.AddSeconds(2);
            var game = Native.FindGame();
            if (game is null)
            {
                this.SetTray("waiting for game");
                return;
            }

            if (game.Id != this.gamePid) { this.AttachTo(game); }
        }

        if (this.handle == IntPtr.Zero) { return; }

        if (this.terrain is not { } current)
        {
            if (DateTime.UtcNow >= this.nextLocate) { this.StartLocate(); }
            return;
        }

        if (this.AreaChanged(current))
        {
            this.LogState("alan degisti");
            this.overlay.SetArea(0);
            this.terrain = null;
            this.lastPos = null;
            this.strikes = 0;
            this.StartLocate();
            return;
        }

        this.EnsureElement();
        this.ReadIconNamesIfMissing(current.StructAddress - GameLayout.TerrainInArea);
        this.LogState();
    }

    /// <summary>Yeni bir oyun sureci (ilk calisma ya da oyun yeniden baslatildi).</summary>
    private void AttachTo(System.Diagnostics.Process game)
    {
        this.CloseOwnHandle();
        this.overlay.ResetGameHandle();
        this.overlay.SetArea(0);
        AreaHolder.Log = message => CrashLog.Write(message, null);

        this.handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, game.Id);
        try { this.moduleBase = (ulong)(game.MainModule?.BaseAddress.ToInt64() ?? 0); }
        catch { this.moduleBase = 0; }

        this.gamePid = game.Id;
        lock (this.knownAreas) { this.knownAreas.Clear(); }
        LocalPlayer.Current = null;
        this.terrain = null;
        this.lastPos = null;
        this.strikes = 0;
        this.failures = 0;
        this.nextLocate = DateTime.MinValue;

        // Sabitlenmis adres surece ozel. Ayni oyun oturumunda daha once sabitlendiyse
        // aday olarak veriyoruz; baska bir surecten kaldiysa hic kullanmiyoruz.
        //
        // Aday, kabul degil: Pin.Run adresi artik HAREKETLE siniyor. Eskiden sadece
        // "makul ve yurunebilir mi" diye bakiyordu; alan degisiminden sonra olmus bir
        // kopya bu sinavi gecti ve harita iki dakika boyunca ayni konumda dondu.
        PlayerChain.Load();
        var seed = PlayerChain.PinnedPid == game.Id ? PlayerChain.PinnedAddress : 0;
        LiveArea.LastPositionAddress = seed;
        if (seed != 0) { LivePosition.Set(new[] { seed }); } else { LivePosition.Clear(); }
        this.LogState($"oyuna baglandi, surec {game.Id}, kayitli adres " +
                      (LiveArea.LastPositionAddress != 0 ? "kullaniliyor" : "yok"));
    }

    private bool AreaChanged(TerrainFinder.Terrain current)
    {
        // Ilk algilayici (PoE1): kalici nesnelerin gosterdigi alan. Oyuncu varligindan once guncelleniyor ve tek okuma.
        if (AreaHolder.Ready)
        {
            var held = AreaHolder.Current(this.handle);
            if (held != 0 && held != current.StructAddress - GameLayout.TerrainInArea) { return true; }
        }

        // Birincil algilayici: oyuncu varliginin gosterdigi alan (varlik + 0x78). Tek okuma,
        // tahmin yok, yukleme ekrani sirasinda uc saniye beklemek de yok.
        if (LocalPlayer.Current is { } tracked)
        {
            var linked = LocalPlayer.CurrentAreaOf(this.handle, tracked.Entity);
            if (PlayerChain.LooksLikePointer(linked) && linked != current.StructAddress - GameLayout.TerrainInArea) { return true; }
        }

        var pos = Player.TryRead(this.handle, this.moduleBase);
        if (pos is not { } p)
        {
            // Yukleme ekraninda konum okunamiyor; uc kez ust uste olursa yeniden yukle.
            return ++this.strikes >= StrikesForAreaChange;
        }

        var jumped = this.lastPos is { } q &&
                     (Math.Abs(p.X - q.X) > AreaJump || Math.Abs(p.Y - q.Y) > AreaJump);
        this.lastPos = p;

        // Oyuncu varligi biliniyorsa ayrac uyanik liste: canli alanin listesi oyuncuyu
        // iceriyor, eski alaninki bosaliyor. Yurunebilirlik sadece yedek - kopru, kapi
        // gibi izgarada kapali gorunen hucrelerde yanlis alarm verebiliyor.
        var stillHere = LocalPlayer.Current is { } lp
            ? LocalPlayer.AwakeContains(this.handle, current.StructAddress - GameLayout.TerrainInArea, lp.Entity)
            : PlayerFinder.Walkable(this.handle, current, p.X, p.Y);

        this.strikes = stillHere ? 0 : this.strikes + 1;
        return jumped || this.strikes >= StrikesForAreaChange;
    }

    /// <summary>
    ///     Oyuncuyu sabitler, canli alanin zeminini okur ve harita ogesini bulur.
    ///     Hepsi arka planda; sonuc arayuz is parcaciginda overlay'e veriliyor.
    /// </summary>
    private void StartLocate()
    {
        this.locateClock.Restart();
        this.busy = true;
        this.SetTray("loading map");

        var h = this.handle;
        var needElement = this.overlay.MapElement == 0;

        Task.Run(() =>
        {
            TerrainFinder.Terrain? area = null;
            byte[]? raw = null;
            UiFinder.UiElement? element = null;

            try
            {
                // 1. Taramasiz yol: oyuncu varligi + 0x78 = yeni alan, zemini + GameLayout.TerrainInArea'da.
                //    Yukleme ekrani surerken alan yapisi henuz hazir olmayabiliyor; birkac
                //    saniye yeniden deniyoruz. Denenecek bir sey yoksa (ilk acilis) hemen tarama.
                var found = this.FastLocate(h);

                // 2. Yedek: butun bellegi tara (ilk acilis ya da taramasiz yol tutmadiysa). Harita ogesi de lazimsa
                //    (ilk acilis) ikisi tek taramada (FirstScan): 14.11.2026 olcum 7.5 sn -> 4.0 sn.
                if (found is null)
                {
                    List<TerrainFinder.Terrain> terrains;
                    if (needElement)
                    {
                        var scan = FirstScan.Run(h, m => CrashLog.Write(m, null));
                        terrains = scan.Terrains;
                        element = scan.Element;
                    }
                    else
                    {
                        terrains = TerrainFinder.Find(h, null);
                    }

                    lock (this.knownAreas)
                    {
                        foreach (var t in terrains) { AddKnown(this.knownAreas, t.StructAddress - GameLayout.TerrainInArea); }
                    }

                    found = LocalPlayer.Locate(h, terrains);
                }

                if (found is { } f)
                {
                    LocalPlayer.Current = new LocalPlayer.Tracked(f.Entity, f.Render);
                    lock (this.knownAreas) { AddKnown(this.knownAreas, f.Area.StructAddress - GameLayout.TerrainInArea); }

                    // Kalici alan tutucularini ogren / dogrula: sonraki alan degisimleri taramadan bulunur.
                    AreaHolder.Observe(h, f.Area.StructAddress - GameLayout.TerrainInArea);
                    area = f.Area;
                }
                else if (Pin.Run(h, this.OnPinMessage) is { } pinned)
                {
                    // Yedek: eski sabitleme (yuruyus ister). Varlik okunamazsa - ornegin bir
                    // patch 0x5D0'i kaydirirsa - harita yine de gelsin.
                    LocalPlayer.Current = null;
                    area = pinned.Area;
                }

                if (area is { } a) { raw = ReadTerrain(h, a); }

                if (raw is not null && needElement && element is null)
                {
                    element = UiFinder.FindLargeMap(h, null, requireVisible: false);
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("harita yukleme", ex);
            }

            this.Post(() => this.FinishLocate(area, raw, element));
        });
    }

    private void OnPinMessage(string message)
    {
        // Pin'in ayrintili kayitlari oyun ekranina yazilmiyor; oyuncunun bilmesi
        // gereken tek sey yurumesi gerektigi.
        if (message.Contains("YURU", StringComparison.OrdinalIgnoreCase))
        {
            this.Post(() => this.overlay.ShowNotice("Walk for a few seconds so the map can be located", 120));
        }
    }

    private void FinishLocate(TerrainFinder.Terrain? area, byte[]? raw, UiFinder.UiElement? element)
    {
        this.busy = false;
        this.overlay.ShowNotice("", 0);

        if (area is not { } pn || raw is null)
        {
            // Alanda degilsen (karakter secimi, yukleme) her denemede bes saniyelik
            // tarama bosa gidiyor; aralari 20 saniyeye kadar aciyoruz.
            this.failures++;
            this.nextLocate = DateTime.UtcNow.AddSeconds(Math.Min(20, 5 * this.failures));
            this.SetTray("map not found, will retry");
            this.LogState($"yukleme basarisiz ({this.failures}. deneme)");
            return;
        }

        this.failures = 0;
        this.terrain = pn;
        this.lastPos = null;
        this.strikes = 0;

        this.overlay.SetMap(raw, pn.BytesPerRow, pn.GridX, pn.GridY);
        this.overlay.SetArea(pn.StructAddress - GameLayout.TerrainInArea);
        this.ReadIconNamesIfMissing(pn.StructAddress - GameLayout.TerrainInArea);
        if (element is { } el) { this.overlay.MapElement = el.Address; }

        // Oge zaten biliniyorsa aranmiyor; "bulunamadi" yazmak gunlugu yalanci yapardi.
        var elementNote = element is not null ? "bulundu"
            : this.overlay.MapElement != 0 ? "biliniyor"
            : "bulunamadi";

        this.SetTray("ready");
        this.LogState($"harita yuklendi ({(LocalPlayer.Current is null ? "yuruyusle" : "varliktan")}) " +
                      $"{this.locateClock.ElapsedMilliseconds} ms, oge {elementNote}");
    }

    /// <summary>
    ///     Buyuk harita ogesinin hala gecerli oldugunu siniyor; degilse arka planda
    ///     yeniden buluyor. Tarama bes saniye surdugu icin en fazla on saniyede bir.
    /// </summary>
    private void EnsureElement()
    {
        if (this.overlay.MapElement != 0)
        {
            if (UiFinder.Reread(this.handle, this.overlay.MapElement) is { } e && UiFinder.IsInUse(this.handle, e))
            {
                return;
            }

            CrashLog.Write($"harita ogesi 0x{this.overlay.MapElement:X} artik kullanilmiyor (oyun yeniden olusturdu), yeniden araniyor", null);
            this.overlay.MapElement = 0;
        }

        if (DateTime.UtcNow < this.nextElementSearch) { return; }
        this.nextElementSearch = DateTime.UtcNow.AddSeconds(10);

        this.busy = true;
        var h = this.handle;
        Task.Run(() =>
        {
            UiFinder.UiElement? found = null;
            try { found = UiFinder.FindLargeMap(h, null, requireVisible: false); }
            catch (Exception ex) { CrashLog.Write("harita ogesi", ex); }

            this.Post(() =>
            {
                this.busy = false;
                if (found is { } f) { this.overlay.MapElement = f.Address; }
            });
        });
    }

    private static byte[]? ReadTerrain(IntPtr h, TerrainFinder.Terrain t)
    {
        if (t.DataLength <= 0 || t.DataLength > int.MaxValue) { return null; }

        var raw = new byte[t.DataLength];
        return Native.ReadProcessMemory(h, (IntPtr)t.DataStart, raw, (IntPtr)t.DataLength, out var got) &&
               (long)got == t.DataLength
            ? raw
            : null;
    }

    private void Post(Action action)
    {
        if (this.overlay.IsDisposed || !this.overlay.IsHandleCreated) { return; }
        try { this.overlay.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    private void SetTray(string state)
    {
        var text = "PoE1 Map - " + state;
        this.tray.Text = text.Length <= 63 ? text : text[..63];
    }

    private void CloseOwnHandle()
    {
        if (this.handle == IntPtr.Zero) { return; }
        Native.CloseHandle(this.handle);
        this.handle = IntPtr.Zero;
    }
}
