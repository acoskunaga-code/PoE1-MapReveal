using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Poe2Map;

/// <summary>
///     Icons.png'de hucresi olmayan yeni simgeler: bunlari kendimiz ciziyoruz. Oyunun tablosunda 9 tane var (16.11.2026,
///     `MapScan icontable &lt;catalog.txt&gt;`: BeyondInitiator, BeyondRealmTransition, AuguryEncounter, DoubleCorruptDevice,
///     AuguryEffigy, ExpeditionObelisk, AuguryBossObject, BetrayalCorpseSculpture, CorruptedPrecursorBeacon).
///
///     Katalogda indeksleri negatif: sayfadan kesilmez, <see cref="Own"/>'daki cizimle uretilir.
///
///     AuguryEncounter = Fate kazani, oyundaki adi "Coven's Cauldron", varlik
///     Metadata/Terrain/Leagues/Augury/Objects/AuguryCardObject (LIVE 16.11.2026 15:5x, `MapScan minimapicons` ve
///     `MapScan paths Augury`). Oyun onu ancak yaklasinca gonderiyor (~2000 birim); alana girerken listede yok.
/// </summary>
internal sealed partial class IconCatalog
{
    private static readonly (string Name, string Label, Action<Graphics, float> Draw)[] Own =
    {
        ("AuguryEncounter", "Fate (Coven's Cauldron)", DrawFate),
    };

    /// <summary>Kendi cizdigimiz simgenin adi -> negatif indeks; degilse null.</summary>
    private static int? OwnIndex(string name)
    {
        for (var i = 0; i < Own.Length; i++)
        {
            if (string.Equals(Own[i].Name, name, StringComparison.OrdinalIgnoreCase)) { return -(i + 1); }
        }

        return null;
    }

    /// <summary>Kendi simgelerimizi kategorilere ve ad tablosuna ekler (catalog.txt'de varsa onun yerine).</summary>
    private static void AddOwn(Dictionary<string, List<Entry>> byCategory, Dictionary<string, int> byName)
    {
        for (var i = 0; i < Own.Length; i++)
        {
            var (name, label, _) = Own[i];
            byName[name] = -(i + 1);
            var category = IconCategories.Of(name);
            if (!byCategory.TryGetValue(category, out var list)) { byCategory[category] = list = new List<Entry>(); }

            list.Insert(0, new Entry(-(i + 1), name, label));
        }
    }

    /// <summary>Negatif indeksli simgeyi size x size ciz; bizim degilse null. Kilit altinda cagriliyor.</summary>
    private static Bitmap? DrawOwn(int index, int size)
    {
        var i = -index - 1;
        if (i < 0 || i >= Own.Length || size <= 0) { return null; }

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        Own[i].Draw(g, size);
        return bitmap;
    }

    /// <summary>Mor daire icinde bir kart: Fate kazani.</summary>
    private static void DrawFate(Graphics g, float s)
    {
        var line = Math.Max(1f, s / 14f);
        var disc = new RectangleF(line, line, s - (2 * line) - 1, s - (2 * line) - 1);
        using (var fill = new SolidBrush(Color.FromArgb(235, 70, 25, 105)))
        using (var edge = new Pen(Color.FromArgb(215, 160, 255), line))
        {
            g.FillEllipse(fill, disc);
            g.DrawEllipse(edge, disc);
        }

        var w = s * 0.36f;
        var h = s * 0.50f;
        var card = new RectangleF((s - w) / 2f, (s - h) / 2f, w, h);
        using (var paper = new SolidBrush(Color.FromArgb(245, 230, 175)))
        using (var border = new Pen(Color.FromArgb(120, 70, 20), Math.Max(1f, s / 24f)))
        {
            g.FillRectangle(paper, card);
            g.DrawRectangle(border, card.X, card.Y, card.Width, card.Height);
        }

        var cx = s / 2f;
        var cy = s / 2f;
        var r = w * 0.28f;
        using var gem = new SolidBrush(Color.FromArgb(150, 50, 200));
        g.FillPolygon(gem, new[] { new PointF(cx, cy - r), new PointF(cx + r, cy), new PointF(cx, cy + r), new PointF(cx - r, cy) });
    }
}
