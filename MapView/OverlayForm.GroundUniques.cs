using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     Yerdeki unique esyalar: haritada unique renginde nokta ve yaninda unique'in adi, tanimlanmamis olsa bile
///     (Heavy Belt -> "Mageblood"). Okuma GroundUniqueReader'da; varlik gezintisi canavarlarla ortak (RefreshMonsters),
///     uyuyan liste dahil. Konum her karede okunuyor. Kullanici istegi 18.11.2026.
///
///     Ayar penceresinin Uniques sekmesi (SetGroundUniques): yalnizca "Show" isaretli unique'in adi yaziliyor (varsayilan
///     hepsi kapali, kullanici 18.11.2026); "Path" isaretli unique yerdeyse ona cikislar gibi yol ciziliyor
///     (OverlayForm.Paths.cs, UniquePathTargets). Yazi tipi ve renk hepsi icin tek. Ayni resmi paylasan unique'ler
///     "A / B" diye geliyor: birinin ayari yetiyor.
/// </summary>
internal sealed partial class OverlayForm
{
    private const string UniqueKeyPrefix = "unique:";
    private static readonly SolidBrush GroundUniqueDot = new(Color.FromArgb(175, 96, 37));

    // Ayarlar butun olarak degistiriliyor (cizim ve arka plan turu eski ya da yeni kumeyi tutarli gorur).
    private HashSet<string> shownUniques = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> pathUniques = new(StringComparer.OrdinalIgnoreCase);
    private Font groundUniqueFont = new("Segoe UI", 10f, FontStyle.Bold);
    private SolidBrush groundUniqueText = new(Color.FromArgb(255, 160, 70));

    /// <summary>Uniques sekmesinden: adi yazilacaklar, yol cizilecekler, yazi tipi ve renk.</summary>
    internal void SetGroundUniques(IEnumerable<string> shown, IEnumerable<string> paths, Font font, Color color)
    {
        this.shownUniques = new HashSet<string>(shown, StringComparer.OrdinalIgnoreCase);
        this.pathUniques = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        if (!font.Equals(this.groundUniqueFont)) { this.groundUniqueFont = (Font)font.Clone(); }

        var key = KeepOffKey(color);
        if (this.groundUniqueText.Color.ToArgb() != key.ToArgb()) { this.groundUniqueText = new SolidBrush(key); }
    }

    private static string[] UniqueNames(string label) => label.Split(" / ", StringSplitOptions.TrimEntries);

    private bool UniqueHasPath(string label) => UniqueNames(label).Any(this.pathUniques.Contains);

    /// <summary>Adi yazilacak mi: adlardan biri isaretliyse ya da yolu isteniyorsa.</summary>
    private bool UniqueShown(string label) => UniqueNames(label).Any(this.shownUniques.Contains) || this.UniqueHasPath(label);

    /// <summary>
    ///     Yolu istenen yerdeki unique'ler, cikis hedefi gibi (RefreshExits, arka planda). Anahtar Render adresi: ayni
    ///     unique yerinden oynamiyor, yol hesabi tekrar kullaniliyor.
    /// </summary>
    private List<TileExits.Target> UniquePathTargets(IntPtr h)
    {
        var targets = new List<TileExits.Target>();
        var position = new byte[8];
        foreach (var (render, _, name) in this.trackedUniques)
        {
            if (!this.UniqueHasPath(name) || !EntityReader.Read(h, render + EntityReader.RenderPosition, position)) { continue; }

            var x = BitConverter.ToSingle(position, 0);
            var y = BitConverter.ToSingle(position, 4);
            if (float.IsFinite(x) && float.IsFinite(y) && x > 0 && y > 0)
            {
                targets.Add(new TileExits.Target($"{UniqueKeyPrefix}{render:X}", name, x, y));
            }
        }

        return targets;
    }

    /// <summary>Unique hedefi hala yerde mi (alindiysa yolu bir sonraki hesabi beklemeden kaybolsun).</summary>
    private bool UniqueStillThere(string key) => this.trackedUniques.Any(u => key == $"{UniqueKeyPrefix}{u.Render:X}");

    // UniqueArt (MapScan'de, CrashLog'u bilmiyor) gunluk satirlarini overlay gunlugune yazsin.
    private static readonly bool UniqueArtLogHooked = HookUniqueArtLog();

    private static bool HookUniqueArtLog()
    {
        UniqueArt.Log = CrashLog.Write;
        return true;
    }

    /// <summary>Yerdeki unique: Render bileseni (konum), WorldItem varligi (oyundaki yazisi, OverlayForm.UniqueFrames.cs), ad.</summary>
    private readonly record struct GroundUnique(ulong Render, ulong Entity, string Name);

    private bool groundUniques = true;
    private GroundUnique[] trackedUniques = Array.Empty<GroundUnique>();
    private readonly byte[] uniquePosition = new byte[8];

    /// <summary>Varlik yerde duran bir unique ise listeye ekler. RefreshMonsters'tan, onbellek kilidi altinda.</summary>
    private void CollectGroundUnique(
        IntPtr h, ulong entity, EntityReader.EntityInfo info, List<GroundUnique> into, HashSet<ulong> done)
    {
        // Yeni dusen esya bir an yarim okunabiliyor (varligin bilesen listesi eksik, esya varligi henuz yok): ilk 20 tur
        // (5 sn) her turda, sonra 8 turda bir (2 sn) yeniden. Bilesen listesi eksikse varlik yeniden okunuyor; onbellekteki
        // yarim kayit kalici olmasin (18.11.2026: envantere alinip yere geri atilan esyanin adi hic cikmiyordu).
        if (info.GroundUnique is null)
        {
            info.GroundUniqueTries++;
            if (info.GroundUniqueTries <= 20 || info.GroundUniqueTries % 8 == 0)
            {
                if (!info.Components.ContainsKey("WorldItem") && EntityReader.ReadInfo(h, entity) is { } fresh)
                {
                    foreach (var (component, address) in fresh.Components) { info.Components[component] = address; }
                }

                info.GroundUnique = GroundUniqueReader.Classify(h, info);
            }
        }

        if (info.GroundUnique is not { Length: > 0 } name) { return; }
        if (!info.Components.TryGetValue("Render", out var render) || !EntityReader.OwnedBy(h, render, entity)) { return; }
        if (!done.Add(render)) { return; }

        into.Add(new GroundUnique(render, entity, name));
    }

    /// <summary>Haritanin donusumuyle unique noktalari ve adlari; en ustte (simgelerden sonra).</summary>
    private void PaintGroundUniques(PaintEventArgs e, Matrix transform)
    {
        var list = this.trackedUniques;
        var h = this.handle;
        if (list.Length == 0 || h == IntPtr.Zero) { return; }

        var points = new PointF[list.Length];
        var valid = new bool[list.Length];
        for (var i = 0; i < list.Length; i++)
        {
            if (!EntityReader.Read(h, list[i].Render + EntityReader.RenderPosition, this.uniquePosition)) { continue; }

            var x = BitConverter.ToSingle(this.uniquePosition, 0);
            var y = BitConverter.ToSingle(this.uniquePosition, 4);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0) { continue; }

            points[i] = new PointF((x / PlayerPos.UnitsPerCell) + this.MapOffsetX, (y / PlayerPos.UnitsPerCell) + this.MapOffsetY);
            valid[i] = true;
        }

        transform.TransformPoints(points);

        var saved = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var width = this.ClientSize.Width;
        var height = this.ClientSize.Height;
        var font = this.groundUniqueFont;
        var text = this.groundUniqueText;
        var lift = font.GetHeight(e.Graphics) / 2f;
        for (var i = 0; i < list.Length; i++)
        {
            var p = points[i];
            if (!valid[i] || p.X < -20 || p.Y < -20 || p.X > width + 20 || p.Y > height + 20) { continue; }
            if (!this.UniqueShown(list[i].Name)) { continue; }

            e.Graphics.FillEllipse(GroundUniqueDot, p.X - 5, p.Y - 5, 10, 10);
            e.Graphics.DrawEllipse(MonsterOutline, p.X - 5, p.Y - 5, 10, 10);
            e.Graphics.DrawString(list[i].Name, font, LabelShadow, p.X + 9, p.Y - lift + 1);
            e.Graphics.DrawString(list[i].Name, font, text, p.X + 8, p.Y - lift);
        }

        e.Graphics.SmoothingMode = saved;
    }
}
