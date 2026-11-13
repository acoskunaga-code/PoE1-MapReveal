using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     Harita simgeleri: oyunun MinimapIcon bileseni olan varliklari (NPC, waypoint, sandik, lig mekanikleri...)
///     ayar penceresindeki Icons sekmesinde verilen boyutta, simge sayfasindaki resimleriyle cizilir. Boyutu 0
///     ya da hic verilmemis simge cizilmez; varsayilan hepsi kapali.
///
///     Canavarlarla ayni iki hiz: varlik listesi saniyede dort kez geziliyor (RefreshMonsters, ayni tur), konum
///     her karede okunuyor. Canavarlardan farki: UYUYAN liste de geziliyor. Oyun gordugu simgeleri (gecilen
///     sandik, uzaktaki NPC) uzaklasinca uyuyan listede tutuyor ve buyuk haritada gostermeye devam ediyor; bu
///     varliklar yerinden oynamadigi icin konumlari bayat degil (siginakta 12.11.2026 canli olcum: hepsinin
///     Render konumu var ve dogru yerde).
///
///     Resimler disaridan geliyor (<see cref="SetMapIcons"/>): bu dosya hangi adin hangi resim oldugunu bilmiyor,
///     sadece (ad, boyut) -> resim soruyor. Donen resim hazir olcekli; cizimde yeniden olceklenmiyor.
/// </summary>
internal sealed partial class OverlayForm
{
    private sealed record MapIconTarget(ulong Render, string Name, Bitmap Sprite, string Label);

    // Simgenin yanindaki ad (shrine / strongbox turu, NPC adi). Cikis adlariyla ayni yazi; renk acik sari.
    private static readonly Font IconLabelFont = new("Segoe UI", 8.5f, FontStyle.Bold);
    private static readonly SolidBrush IconLabelBrush = new(Color.FromArgb(255, 235, 170));

    // Ad -> boyut (piksel). Tek nesne olarak degistiriliyor: ayari arayuz is parcacigi yazarken arka plan okumasi
    // yarim bir sozluk gormesin.
    private IReadOnlyDictionary<string, int>? mapIconSizes;
    private Func<string, int, Bitmap?>? mapIconSprite;
    private bool mapIconLabels = true;
    private MapIconTarget[] trackedIcons = Array.Empty<MapIconTarget>();
    private readonly byte[] iconPosition = new byte[8];
    private int paintedIcons;
    private string lastIconSummary = "";
    private long nextIconSummary;

    /// <summary>
    ///     Simge boyutlari (ad -> piksel, 0 = kapali) ve resim kaynagi. Sozluk kopyalaniyor, cagiran kendi
    ///     sozlugunu degistirmeye devam edebilir. Kaynak null ya da hepsi kapaliysa simge okunmaz.
    /// </summary>
    internal void SetMapIcons(IReadOnlyDictionary<string, int> sizes, Func<string, int, Bitmap?>? sprite, bool labels)
    {
        this.mapIconLabels = labels;
        var copy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, size) in sizes)
        {
            if (size > 0) { copy[name] = size; }
        }

        this.mapIconSprite = sprite;
        this.mapIconSizes = copy.Count > 0 && sprite is not null ? copy : null;
    }

    /// <summary>Varligin simgesi aciksa listeye ekler. RefreshMonsters'tan, onbellek kilidi altinda.</summary>
    private void CollectIcon(
        IntPtr h, ulong entity, EntityReader.EntityInfo info, IReadOnlyDictionary<string, int> sizes,
        List<MapIconTarget> into, HashSet<ulong> done)
    {
        if (!info.Components.ContainsKey(MapIconReader.ComponentName)) { return; }

        // Gizli: oyun simgeyi kaldirmis (acilan strongbox, alinan shrine).
        if (MapIconReader.Read(h, entity, info) is not { Hidden: false } state) { return; }
        if (!sizes.TryGetValue(state.Name, out var size) || size <= 0) { return; }

        // Canavarlardaki gibi: bilesenlerinin sahibi olmayan bayat kopya ayni yere ikinci simge cizerdi.
        if (!info.Components.TryGetValue("Render", out var render) || !EntityReader.OwnedBy(h, render, entity)) { return; }
        if (!done.Add(render)) { return; }

        if (this.mapIconSprite?.Invoke(state.Name, size) is not { } sprite) { return; }

        var label = this.mapIconLabels ? MapIconReader.DisplayName(h, info) : "";
        into.Add(new MapIconTarget(render, state.Name, sprite, label));
    }

    /// <summary>Simgeleri haritanin donusumuyle cizer; canavarlarin ustune, oyuncu isaretinin altina.</summary>
    private void PaintMapIcons(PaintEventArgs e, Matrix transform)
    {
        var list = this.trackedIcons;
        var h = this.handle;
        if (list.Length == 0 || h == IntPtr.Zero)
        {
            this.paintedIcons = 0;
            return;
        }

        var points = new PointF[list.Length];
        var valid = new bool[list.Length];
        for (var i = 0; i < list.Length; i++)
        {
            if (!EntityReader.Read(h, list[i].Render + EntityReader.RenderPosition, this.iconPosition)) { continue; }

            var x = BitConverter.ToSingle(this.iconPosition, 0);
            var y = BitConverter.ToSingle(this.iconPosition, 4);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0) { continue; }

            points[i] = new PointF((x / PlayerPos.UnitsPerCell) + this.MapOffsetX, (y / PlayerPos.UnitsPerCell) + this.MapOffsetY);
            valid[i] = true;
        }

        transform.TransformPoints(points);

        var width = this.ClientSize.Width;
        var height = this.ClientSize.Height;
        var painted = 0;

        for (var i = 0; i < list.Length; i++)
        {
            if (!valid[i]) { continue; }

            // Resim zaten istenen boyutta: tam piksele oturtup 1:1 ciziliyor, bulaniklik yok.
            var sprite = list[i].Sprite;
            var left = (int)MathF.Round(points[i].X - (sprite.Width / 2f));
            var top = (int)MathF.Round(points[i].Y - (sprite.Height / 2f));
            if (left + sprite.Width < 0 || top + sprite.Height < 0 || left > width || top > height) { continue; }

            e.Graphics.DrawImage(sprite, left, top, sprite.Width, sprite.Height);
            painted++;

            var label = list[i].Label;
            if (label.Length == 0) { continue; }

            // Saydamlik anahtari saf siyah: kenar yumusatmasi siyaha karisip yaziyi bulaniklastirir, o yuzden
            // tek bit yazi ve koyu golge (OverlayForm.Paths.cs'teki cikis adlariyla ayni yol).
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            var x = left + sprite.Width + 2;
            var y = points[i].Y - 7;
            e.Graphics.DrawString(label, IconLabelFont, LabelShadow, x + 1, y + 1);
            e.Graphics.DrawString(label, IconLabelFont, IconLabelBrush, x, y);
        }

        this.paintedIcons = painted;
    }

    /// <summary>
    ///     Gunluk: hangi simgeler izleniyor ve son karede kaci cizildi. Sadece degisince, en fazla bes saniyede
    ///     bir: "simge yok" sikayetinde ilk bakilacak satir.
    /// </summary>
    private void LogIconSummary(List<MapIconTarget> found)
    {
        if (this.mapIconSizes is null && found.Count == 0 && this.lastIconSummary.Length == 0) { return; }

        var names = found.GroupBy(t => t.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}x{g.Count()}");
        var summary = $"simgeler: {found.Count} izleniyor ({string.Join(" ", names)})";
        var now = Environment.TickCount64;
        if (summary == this.lastIconSummary || now < this.nextIconSummary) { return; }

        this.lastIconSummary = summary;
        this.nextIconSummary = now + 5000;
        CrashLog.Write($"{summary}, son karede cizilen {this.paintedIcons}", null);
    }
}
