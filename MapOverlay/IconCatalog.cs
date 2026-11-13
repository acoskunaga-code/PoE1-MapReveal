using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Poe2Map;

/// <summary>
///     Ayar sekmesinin simge katalogu: simge adlari (catalog.txt) ve simge sayfasi (Icons.png). Ikisi de depoda yok:
///     resim ilk acilista indiriliyor (IconDownload), adlar oyunun kendi tablosundan okunuyor (IconTable). Aranan yer: &lt;exe klasoru&gt;\data\icons\
///     ve, exe klasoru yazilamiyorsa, AppPaths'in yedek klasoru.
///
///     catalog.txt yoksa katalog yok (sekme aciklama gosterir); Icons.png yoksa adlar duruyor, onizleme bos.
///     Sayfa duzeni (ExileApi'nin Icons.png'si): 64x64 hucre, 14 sutun, indeks 1 = ilk hucre. Kategoriler
///     <see cref="IconCategories"/>'ten; loot filtresi sekilleri listeye alinmiyor (ad aramasinda duruyorlar).
///
///     Iki is parcacigindan kullaniliyor: onizleme arayuzden, haritaya cizilecek resim (<see cref="Sprite"/>)
///     overlay'in arka plan okumasindan. GDI+ ayni Bitmap'e ayni anda iki yerden dokunulmasina izin vermiyor,
///     o yuzden sayfaya her erisim tek kilit altinda.
/// </summary>
internal sealed class IconCatalog : IDisposable
{
    internal const int ThumbSize = 32;

    private const int CellSize = 64;
    private const int Columns = 14;

    // Ayni anda tutulan cizim resmi sayisinin ust siniri. Cetvel surukledikce her boyut icin yeni resim
    // uretiliyor; sinir asilinca onbellek bosaltiliyor. Resimler Dispose EDILMIYOR: o anda cizimde
    // kullaniliyor olabilirler, cop toplayici kapatiyor.
    private const int MaxSprites = 600;

    private readonly object sync = new();
    private readonly Bitmap? atlas;
    private readonly Dictionary<int, Bitmap> thumbs = new();
    private readonly Dictionary<(int Index, int Size), Bitmap> sprites = new();
    private readonly IReadOnlyDictionary<string, int> byName;
    private readonly HashSet<string> unknownNames = new(StringComparer.Ordinal);

    private IconCatalog(IReadOnlyList<Category> categories, IReadOnlyDictionary<string, int> byName, Bitmap? atlas)
    {
        this.Categories = categories;
        this.byName = byName;
        this.atlas = atlas;
    }

    internal sealed record Entry(int Index, string Name, string Label);

    internal sealed record Category(string Name, IReadOnlyList<Entry> Entries);

    internal IReadOnlyList<Category> Categories { get; }

    internal bool HasImages => this.atlas != null;

    internal static IconCatalog? Load()
    {
        // Resmi de olan klasor once: exe yaninda yalniz catalog.txt varsa indirilen tam veri yine kullanilsin.
        var folders = Folders().ToList();
        foreach (var folder in folders.Where(f => File.Exists(Path.Combine(f, "Icons.png"))).Concat(folders))
        {
            var file = Path.Combine(folder, "catalog.txt");
            if (!File.Exists(file)) { continue; }

            try
            {
                var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var categories = Parse(File.ReadLines(file), byName);
                if (categories.Count == 0) { continue; }

                return new IconCatalog(categories, byName, LoadAtlas(Path.Combine(folder, "Icons.png")));
            }
            catch (Exception ex)
            {
                CrashLog.Write("simge katalogu okunamadi: " + ex.Message, null);
            }
        }

        return null;
    }

    /// <summary>Simgenin onizlemesi (ThumbSize kare); sayfa yoksa ya da hucre sayfanin disindaysa null.</summary>
    internal Bitmap? Thumbnail(Entry entry)
    {
        lock (this.sync)
        {
            if (this.atlas is null) { return null; }
            if (this.thumbs.TryGetValue(entry.Index, out var cached)) { return cached; }

            var thumb = this.Cut(entry.Index, ThumbSize);
            if (thumb is not null) { this.thumbs[entry.Index] = thumb; }

            return thumb;
        }
    }

    /// <summary>
    ///     Oyundaki simge adiyla (MinimapIcons.dat Id'si) haritaya cizilecek resim, size x size piksel. Ad katalogda
    ///     yoksa ya da sayfa yoksa null; bilinmeyen her ad gunluge bir kez yaziliyor (oyun yeni simge eklemis olabilir).
    /// </summary>
    internal Bitmap? Sprite(string name, int size)
    {
        if (this.atlas is null || size <= 0) { return null; }

        size = Math.Min(size, OverlaySettings.IconMaxSize);
        lock (this.sync)
        {
            if (!this.byName.TryGetValue(name, out var index))
            {
                if (this.unknownNames.Add(name)) { CrashLog.Write("simge adi katalogda yok: " + name, null); }
                return null;
            }

            if (this.sprites.TryGetValue((index, size), out var cached)) { return cached; }

            var sprite = this.Cut(index, size);
            if (sprite is null) { return null; }

            KeepOffColourKey(sprite);
            if (this.sprites.Count >= MaxSprites) { this.sprites.Clear(); }

            this.sprites[(index, size)] = sprite;
            return sprite;
        }
    }

    public void Dispose()
    {
        lock (this.sync)
        {
            foreach (var thumb in this.thumbs.Values) { thumb.Dispose(); }

            this.thumbs.Clear();
            this.sprites.Clear();
            this.atlas?.Dispose();
        }
    }

    /// <summary>Sayfadan bir hucre, size x size'a yuksek kaliteyle olceklenmis. Kilit altinda cagriliyor.</summary>
    private Bitmap? Cut(int index, int size)
    {
        if (this.atlas is null || index <= 0) { return null; }

        var cell = index - 1;
        var source = new Rectangle((cell % Columns) * CellSize, (cell / Columns) * CellSize, CellSize, CellSize);
        if (source.Right > this.atlas.Width || source.Bottom > this.atlas.Height) { return null; }

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(this.atlas, new Rectangle(0, 0, size, size), source, GraphicsUnit.Pixel);
        return bitmap;
    }

    /// <summary>
    ///     Overlay penceresi saf siyahi saydam sayiyor (TransparencyKey). Simgenin opak siyah pikselleri delik
    ///     olmasin diye (1, 1, 1)'e cekiliyor; goze yine siyah.
    /// </summary>
    private static void KeepOffColourKey(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            for (var i = 0; i < pixels.Length; i++)
            {
                if ((pixels[i] & 0x00FFFFFF) == 0 && (pixels[i] & unchecked((int)0xFF000000)) != 0)
                {
                    pixels[i] |= 0x00010101;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static IEnumerable<string> Folders()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "data", "icons");
        yield return IconDownload.Folder;
    }

    /// <summary>
    ///     Satirlar "indeks TAB ad"; # ile baslayanlar ve bozuk satirlar atlaniyor. Butun adlar (loot filtresi
    ///     sekilleri dahil) <paramref name="byName"/>'e, sekmede gosterilecekler kategorilere.
    /// </summary>
    private static List<Category> Parse(IEnumerable<string> lines, Dictionary<string, int> byName)
    {
        var byCategory = new Dictionary<string, List<Entry>>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') { continue; }

            var tab = line.IndexOf('\t');
            if (tab <= 0 || !int.TryParse(line[..tab], out var index) || index <= 0) { continue; }

            var name = line[(tab + 1)..].Trim();
            if (name.Length == 0) { continue; }

            byName.TryAdd(name, index);
            var category = IconCategories.Of(name);
            if (category == IconCategories.LootFilter) { continue; }

            if (!byCategory.TryGetValue(category, out var list)) { byCategory[category] = list = new List<Entry>(); }

            list.Add(new Entry(index, name, IconCategories.Pretty(name)));
        }

        var result = new List<Category>();
        foreach (var name in IconCategories.Order.Append(IconCategories.Other))
        {
            if (byCategory.TryGetValue(name, out var list)) { result.Add(new Category(name, list)); }
        }

        return result;
    }

    /// <summary>Dosyayi bellege kopyaliyor: pencere acikken dosya kilitli kalmasin.</summary>
    private static Bitmap? LoadAtlas(string file)
    {
        if (!File.Exists(file)) { return null; }

        try
        {
            using var raw = new Bitmap(file);
            return new Bitmap(raw);
        }
        catch
        {
            return null;
        }
    }
}
