using System.Drawing.Text;

namespace Poe2Map;

/// <summary>
///     Fate secim paneli: kartlarin odulleri okunur (FateLabels) ama ekrana YAZILMAZ. 22.11.2026, kullanici: "fate
///     ekranindaki yazilar istege bagli degil kalici olarak kapattin degilmi". Cizilenler: TAKE, COPY, tiklama
///     numaralari, kampanya bossu ve degerli odul etiketleri, aranan yazi gecen kartin cercevesi.
///
///     Panel arayuzde surekli duruyor (kapaliyken gizli); bir kez bulunup yolu kaydediliyor, sonra her taramada sadece
///     gorunur mu diye bakiliyor. Kapaliyken maliyeti birkac okuma.
/// </summary>
internal sealed partial class OverlayForm
{
    private ulong fatePanel;
    private ulong fatePanelRoot;
    private int[]? fatePath;
    private bool fatePathLoaded;
    private bool fatePathFailed;
    private long nextFateSearch;
    private int fateSearching;
    private FateLabels.Card[] fateCards = Array.Empty<FateLabels.Card>();
    private Font? fateFont;

    // Ayar penceresinin Fates sekmesi: odulunde bu yazilardan biri gecen kart bu renkte cercevelenir. Kalem butun
    // olarak degistiriliyor, eskisi Dispose edilmiyor (o anda cizimde olabilir).
    private string[] fatePhrases = Array.Empty<string>();
    private Pen fateMatchFrame = new(Color.FromArgb(255, 200, 40), 3f);

    /// <summary>
    ///     Fate ayarlari. Yazilar odullerle ayni bicime getiriliyor: oyunun etiketleri atiliyor, "Empowered Monster"
    ///     "Monster" oluyor (FateLabels.Clean), sayilar "#" (AltarLabels.Normalize); 3 harften kisa kalanlar atiliyor.
    /// </summary>
    internal void SetFateFrames(IEnumerable<string> phrases, Color color)
    {
        this.fatePhrases = phrases.Select(p => AltarLabels.Normalize(FateLabels.Clean(p)))
            .Where(p => p.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var key = KeepOffKey(color);
        if (this.fateMatchFrame.Color.ToArgb() != key.ToArgb()) { this.fateMatchFrame = new Pen(key, 3f); }
    }

    /// <summary>Odulde aranan yazilardan biri geciyor mu.</summary>
    private bool FateMatches(string reward)
    {
        var phrases = this.fatePhrases;
        if (phrases.Length == 0) { return false; }

        var normal = AltarLabels.Normalize(reward);
        return phrases.Any(p => normal.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Arka plan, saniyede dort kez (RefreshAltars ile ayni yerden): acik kartlarin yazilari.</summary>
    private void RefreshFates(IntPtr h)
    {
        // Oneri kapali olsa da okunuyor: kampanya bossu, degerli odul ve aranan yazi isaretleri kartlardan geliyor.
        if (h == IntPtr.Zero || this.MapElement == 0)
        {
            this.fateCards = Array.Empty<FateLabels.Card>();
            return;
        }

        var root = AltarLabels.RootOf(h, this.MapElement);
        if (this.fatePanel != 0 && root != this.fatePanelRoot)
        {
            CrashLog.Write($"fate paneli birakildi ({this.fatePanel:X}): arayuz koku degisti", null);
            this.fatePanel = 0;
        }

        if (this.fatePanel == 0) { this.ResolveFatePanel(h, root); }

        if (this.fatePanel != 0)
        {
            if (FateLabels.Scan(h, this.fatePanel) is { } cards)
            {
                this.fateCards = cards.ToArray();
                this.UpdateFatePlan(h, cards);
                return;
            }

            CrashLog.Write($"fate paneli birakildi ({this.fatePanel:X}): kendi adresini tutmuyor, yeniden aranacak", null);
            this.fatePanel = 0;
            this.nextFateSearch = 0;
        }

        this.fateCards = Array.Empty<FateLabels.Card>();
        this.SearchFatePanel(h, root);
    }

    /// <summary>Kayitli yoldan (dosyadan bir kez okunuyor). Yol bu oturumda yanlis ciktiysa arama yenisini yazana kadar kullanilmiyor.</summary>
    private void ResolveFatePanel(IntPtr h, ulong root)
    {
        if (!this.fatePathLoaded)
        {
            this.fatePathLoaded = true;
            this.fatePath = AltarLabels.LoadPath(FateLabels.PathFile);
        }

        if (this.fatePath is not { } path || this.fatePathFailed) { return; }

        var panel = AltarLabels.Resolve(h, root, path);
        if (panel != 0 && FateLabels.LooksLikePanel(h, panel))
        {
            this.fatePanelRoot = root;
            this.fatePanel = panel;
            return;
        }

        CrashLog.Write($"fate paneli kayitli yolda yok ({string.Join('/', path)}), aranacak", null);
        this.fatePathFailed = true;
    }

    /// <summary>Arka planda agac taramasi; en fazla 30 saniyede bir (panel bulunana kadar).</summary>
    private void SearchFatePanel(IntPtr h, ulong root)
    {
        var now = Environment.TickCount64;
        if (now < this.nextFateSearch || Interlocked.Exchange(ref this.fateSearching, 1) == 1) { return; }

        this.nextFateSearch = now + 30_000;
        Task.Run(() =>
        {
            try
            {
                var started = Environment.TickCount64;
                var panel = FateLabels.FindPanel(h, root);
                if (panel == 0) { return; }

                var path = AltarLabels.PathOf(h, root, panel);
                if (path is not null && (this.fatePath is null || !path.SequenceEqual(this.fatePath)))
                {
                    this.fatePath = path;
                    AltarLabels.SavePath(
                        FateLabels.PathFile,
                        path,
                        "# Fate secim panelinin arayuz kokunden cocuk sirasi yolu. Overlay tuy yardim yazisiyla bulup yaziyor;",
                        "# yamadan sonra tutmazsa kendiliginden yeniden bulur ve bu satiri gunceller.");
                }

                this.fatePathFailed = false;
                this.fatePanelRoot = root;
                this.fatePanel = panel;
                CrashLog.Write($"fate paneli bulundu: {panel:X} yol {(path is null ? "?" : string.Join('/', path))} " +
                               $"({Environment.TickCount64 - started} ms)", null);
            }
            catch (Exception ex)
            {
                CrashLog.Write("fate paneli aramasi", ex);
            }
            finally
            {
                Interlocked.Exchange(ref this.fateSearching, 0);
            }
        });
    }

    /// <summary>Her karede: kartlarin ustundeki isaretler; hepsi kartin kendi kutusunda, disari yazi yok.</summary>
    private void PaintFates(PaintEventArgs e)
    {
        var cards = this.fateCards;
        var h = this.handle;
        if (cards.Length == 0 || h == IntPtr.Zero) { return; }

        var width = this.ClientSize.Width;
        var height = this.ClientSize.Height;
        var size = Math.Clamp(11f * height / 1017f, 8f, 18f);
        if (this.fateFont is not { } font || Math.Abs(font.Size - size) > 0.1f)
        {
            font = this.fateFont = new Font("Segoe UI", size, FontStyle.Regular);
        }

        // Fare bir kartin ustundeyken oyunun kart aciklamasi butun kartlarin ustune aciliyor (14.11.2026): o sirada hicbir sey yok.
        var mouse = GetCursorPos(out var screenMouse) ? this.PointToClient(screenMouse) : new Point(-1, -1);

        var placed = new List<(FateLabels.Card Card, RectangleF Rect, bool Hovered)>(cards.Length);
        foreach (var card in cards)
        {
            if (card.Rewards.Count == 0 || this.CardRect(h, card, width, height) is not { } rect) { continue; }
            if (rect.Width >= 20) { placed.Add((card, rect, rect.Contains(mouse))); }
        }

        if (placed.Count == 0 || placed.Any(p => p.Hovered)) { return; }

        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        this.PaintFateMatches(e.Graphics, placed);

        // Ek kampanya bossu cikaran kartlar (OverlayForm.FateBoss.cs); numaralar onlarin ustune.
        PaintBossFates(e.Graphics, placed, font);

        // 3.30.0c'nin yeni degerli odulleri (OverlayForm.FateChase.cs): Divine, Mirror Shard, Fracturing Orb, Hinekora.
        PaintChaseFates(e.Graphics, placed, font);

        // En iyi secim: TAKE, COPY ve numaralar.
        this.PaintFatePlan(e.Graphics, placed, height);
    }

    /// <summary>Odulunde aranan yazilardan biri gecen kart: TAKE ve COPY cercevelerinin disinda, ayar renginde cerceve.</summary>
    private void PaintFateMatches(Graphics g, List<(FateLabels.Card Card, RectangleF Rect, bool Hovered)> placed)
    {
        foreach (var (card, rect, _) in placed)
        {
            if (!card.Rewards.Any(this.FateMatches)) { continue; }

            var frame = RectangleF.Inflate(rect, 13, 13);
            g.DrawRectangle(this.fateMatchFrame, frame.X, frame.Y, frame.Width, frame.Height);
            frame.Inflate(2.5f, 2.5f);
            g.DrawRectangle(AltarFrameEdge, frame.X, frame.Y, frame.Width, frame.Height);
        }
    }

    /// <summary>Kartin ekrandaki tam kutusu: govde, ad seridi ve odul simgeleri ogelerinin birlesimi (FateLabels.Card.Parts).</summary>
    private RectangleF? CardRect(IntPtr h, FateLabels.Card card, int width, int height) => PartsRect(h, card.Parts, width, height);

    /// <summary>Ogelerin ekrandaki kutularinin birlesimi; hicbiri okunamazsa null.</summary>
    private static RectangleF? PartsRect(IntPtr h, IEnumerable<ulong> parts, int width, int height)
    {
        RectangleF? all = null;
        foreach (var part in parts)
        {
            if (UiFinder.Reread(h, part) is not { } el || UiFinder.ElementPosition(h, part, width, height) is not { } at) { continue; }

            var (sx, sy) = UiFinder.ScaleValue(el.ScaleIndex, el.LocalScale, width, height, 0);
            var rect = new RectangleF(at.X, at.Y, el.SizeX * sx, el.SizeY * sy);
            if (rect.Width <= 0 || rect.Height <= 0) { continue; }

            all = all is { } a ? RectangleF.Union(a, rect) : rect;
        }

        return all;
    }
}
