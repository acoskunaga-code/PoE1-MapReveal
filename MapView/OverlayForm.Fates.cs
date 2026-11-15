using System.Drawing.Text;

namespace Poe2Map;

/// <summary>
///     Fate secim paneli: her kartin ustune, kartin altindaki yuvarlak simgelerin yazilari (FateLabels) yaziliyor;
///     panel acildigi anda, fareyle uzerine gelmeden. Buyuk harita kapaliyken de ciziliyor.
///
///     Panel arayuzde surekli duruyor (kapaliyken gizli); bir kez bulunup yolu kaydediliyor, sonra her taramada sadece
///     gorunur mu diye bakiliyor. Kapaliyken maliyeti birkac okuma.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly SolidBrush FateBack = new(Color.FromArgb(255, 16, 12, 8));
    private static readonly SolidBrush FateText = new(Color.FromArgb(240, 228, 200));
    private static readonly Pen FateEdge = new(Color.FromArgb(200, 160, 90), 1f);

    private ulong fatePanel;
    private ulong fatePanelRoot;
    private int[]? fatePath;
    private bool fatePathLoaded;
    private bool fatePathFailed;
    private long nextFateSearch;
    private int fateSearching;
    private FateLabels.Card[] fateCards = Array.Empty<FateLabels.Card>();
    private Font? fateFont;

    // Ayar penceresinin Fates sekmesi: yazilar acik mi, hangi yazilar gecen odul renkli + kutusu cerceveli, hangi renkle.
    // Firca ve kalem butun olarak degistiriliyor, eskisi Dispose edilmiyor (o anda cizimde olabilir).
    private bool fateEnabled = true;
    private string[] fatePhrases = Array.Empty<string>();
    private SolidBrush fateMatchText = new(Color.FromArgb(255, 200, 40));
    private Pen fateMatchFrame = new(Color.FromArgb(255, 200, 40), 3f);

    /// <summary>
    ///     Fate ayarlari. Yazilar odullerle ayni bicime getiriliyor: oyunun etiketleri atiliyor, "Empowered Monster"
    ///     "Monster" oluyor (FateLabels.Clean), sayilar "#" (AltarLabels.Normalize); 3 harften kisa kalanlar atiliyor.
    /// </summary>
    internal void SetFateLabels(bool enabled, IEnumerable<string> phrases, Color color)
    {
        this.fateEnabled = enabled;
        this.fatePhrases = phrases.Select(p => AltarLabels.Normalize(FateLabels.Clean(p)))
            .Where(p => p.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var key = KeepOffKey(color);
        if (this.fateMatchText.Color.ToArgb() != key.ToArgb())
        {
            this.fateMatchText = new SolidBrush(key);
            this.fateMatchFrame = new Pen(key, 3f);
        }

        if (!enabled && !this.fatePlanEnabled) { this.fateCards = Array.Empty<FateLabels.Card>(); }
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
        // Odul yazilari kapali olsa da en iyi secim onerisi (OverlayForm.FatePlan.cs) acikken kartlar okunuyor.
        if ((!this.fateEnabled && !this.fatePlanEnabled) || h == IntPtr.Zero || this.MapElement == 0)
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

    /// <summary>
    ///     Her karede: her acik kartin ustune bir kutu icinde odul yazilari. Kutu genisligi kart genisligi ile kartlar
    ///     arasi adimin kucugu: govde ogesi gorunen karttan genis (1920x1017'de ~212 px, kartlar arasi adim kadar), kutular
    ///     yan yana oturuyor ama birbirine binmiyor. Kutu asagidan karta dayaniyor, yukari dogru uzuyor.
    /// </summary>
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

        // Fare bir kartin ustundeyken oyun o kartin kendi aciklamasini ayni yere, kartin ustune aciyor (14.11.2026: kutumuz
        // onu kapatiyordu). O kartin kutusu o sirada cizilmiyor.
        var mouse = GetCursorPos(out var screenMouse) ? this.PointToClient(screenMouse) : new Point(-1, -1);

        var placed = new List<(FateLabels.Card Card, RectangleF Rect, bool Hovered)>(cards.Length);
        foreach (var card in cards)
        {
            if (card.Rewards.Count == 0 || this.CardRect(h, card, width, height) is not { } rect) { continue; }
            if (rect.Width >= 20) { placed.Add((card, rect, rect.Contains(mouse))); }
        }

        // Fare bir kartin ustundeyken oyunun kart aciklamasi ekranin ust kismina, butun kartlarin uzerine genis aciliyor
        // (14.11.2026 goruntusu: "Rotted" aciklamasi diger iki kartin kutularinin altinda kaldi). O sirada hic kutu yok.
        if (placed.Count == 0 || placed.Any(p => p.Hovered)) { return; }

        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var cardRects = placed.Select(p => p.Rect).ToList();
        var boxes = new List<RectangleF>();
        foreach (var (card, rect, hovered) in placed)
        {
            if (hovered || !this.fateEnabled) { continue; }

            // Kutu genisligi AYNI SIRADAKI kartlara gore (ust uste iki sira kart olabiliyor: secim kartlari ve alttaki
            // toplanmis Fate'ler); butun kartlara gore olcunce iki siranin yakin merkezleri kutuyu daraltiyordu.
            var boxWidth = RowPitch(placed.Select(p => p.Rect), rect);

            // Her odul ayri olculup ayri fircayla yaziliyor (aranan yazi gecen odul ayar rengiyle); oduller arasi bosluk.
            var lines = card.Rewards
                .Select(r => (Text: "• " + r.Replace("\n", "\n   "), Match: this.FateMatches(r)))
                .Select(l => (l.Text, l.Match, Height: e.Graphics.MeasureString(l.Text, font, (int)(boxWidth - 10)).Height))
                .ToList();
            var textHeight = lines.Sum(l => l.Height) + (Math.Max(0, lines.Count - 1) * RewardGap);
            var box = this.PlaceFateBox(rect, boxWidth, textHeight + 10, cardRects, boxes, height);
            boxes.Add(box);

            e.Graphics.FillRectangle(FateBack, box);
            e.Graphics.DrawRectangle(FateEdge, box.X, box.Y, box.Width, box.Height);

            var top = box.Y + 5;
            foreach (var (text, match, lineHeight) in lines)
            {
                e.Graphics.DrawString(text, font, match ? this.fateMatchText : FateText, new RectangleF(box.X + 5, top, box.Width - 10, lineHeight + 2));
                top += lineHeight + RewardGap;
            }

            // Aranan yazilardan biri gecen kartin kutusu kalin cerceve (altarlardaki gibi), disinda ince koyu kenar.
            if (lines.Any(l => l.Match))
            {
                var frame = box;
                frame.Inflate(2, 2);
                e.Graphics.DrawRectangle(this.fateMatchFrame, frame.X, frame.Y, frame.Width, frame.Height);
                frame.Inflate(2.5f, 2.5f);
                e.Graphics.DrawRectangle(AltarFrameEdge, frame.X, frame.Y, frame.Width, frame.Height);
            }
        }

        // En iyi secim: numaralar ve "Best" kutusu, odul kutularinin yerleri belli olduktan sonra (onlara binmesin).
        this.PaintFatePlan(e.Graphics, placed, boxes, font, height);
    }

    private const float RewardGap = 4f;

    /// <summary>
    ///     Kartin sirasindaki (ust kenari 30 px icinde) kartlarla: kart genisligi ile en yakin iki merkez arasinin kucugu,
    ///     8 px bosluk birakarak; en az 100 px.
    /// </summary>
    private static float RowPitch(IEnumerable<RectangleF> cards, RectangleF card)
    {
        var row = cards.Where(c => Math.Abs(c.Y - card.Y) < 30).Select(c => c.X + (c.Width / 2f)).OrderBy(x => x).ToList();
        var pitch = card.Width;
        for (var i = 1; i < row.Count; i++) { pitch = Math.Min(pitch, row[i] - row[i - 1]); }

        return Math.Max(pitch - 8f, 100f);
    }

    /// <summary>Kartin ekrandaki tam kutusu: govde, ad seridi ve odul simgeleri ogelerinin birlesimi (FateLabels.Card.Parts).</summary>
    private RectangleF? CardRect(IntPtr h, FateLabels.Card card, int width, int height)
    {
        RectangleF? all = null;
        foreach (var part in card.Parts)
        {
            if (UiFinder.Reread(h, part) is not { } el || UiFinder.ElementPosition(h, part, width, height) is not { } at) { continue; }

            var (sx, sy) = UiFinder.ScaleValue(el.ScaleIndex, el.LocalScale, width, height, 0);
            var rect = new RectangleF(at.X, at.Y, el.SizeX * sx, el.SizeY * sy);
            if (rect.Width <= 0 || rect.Height <= 0) { continue; }

            all = all is { } a ? RectangleF.Union(a, rect) : rect;
        }

        return all;
    }

    /// <summary>
    ///     Kutunun yeri, sirayla ilk uyan: kartin ustu, alti, sagi, solu. "Uyan" = baska bir karta ya da daha once konan bir
    ///     kutuya binmiyor ve ekranin icinde. 14.11.2026: dort kartli panelde alttaki kartin ustu ortadaki kartin
    ///     simgelerine biniyordu, alti ekrandan tasiyordu; yanlari bostu. Hicbiri uymazsa ust, ekranin ustune sigdirilarak.
    /// </summary>
    private RectangleF PlaceFateBox(RectangleF card, float boxWidth, float boxHeight, List<RectangleF> cards, List<RectangleF> boxes, int screenHeight)
    {
        var x = card.X + (card.Width / 2f) - (boxWidth / 2f);
        var screen = new RectangleF(0, 0, this.ClientSize.Width, screenHeight);
        var options = new[]
        {
            new RectangleF(x, card.Y - boxHeight - 4, boxWidth, boxHeight),
            new RectangleF(x, card.Bottom + 4, boxWidth, boxHeight),
            new RectangleF(card.Right + 4, card.Y, boxWidth, boxHeight),
            new RectangleF(card.X - boxWidth - 4, card.Y, boxWidth, boxHeight),
        };

        foreach (var option in options)
        {
            if (screen.Contains(option) && !Collides(option, card, cards, boxes)) { return option; }
        }

        var above = options[0];
        if (above.Top < 0) { above.Y = 0; }
        return above;
    }

    private static bool Collides(RectangleF box, RectangleF own, List<RectangleF> cards, List<RectangleF> boxes) =>
        cards.Any(c => c != own && c.IntersectsWith(box)) || boxes.Any(b => b.IntersectsWith(box));
}
