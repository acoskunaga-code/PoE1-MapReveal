using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Poe2Map;

/// <summary>
///     Zemin onbellegi (kullanici 21.11.2026: "maprevealimiz pc'ye yuk bindiriyor mu"). Olcum: harita acikken overlay bir
///     cekirdegin %70-85'ini yiyordu, kareler 25-38 ms; arayuz is parcacigi tek basina %79. Sebep: her karede butun
///     harita bitmap'i (orn. 3818x1932) donusumle (dondurme + olcek) ekrana ciziliyordu.
///
///     Simdi harita bir kez, donusmus haliyle, pencerenin her yonden dortte biri kadar genis bir bitmap'e ciziliyor; her
///     karede onun GDI kopyasindan pencere kadar bir parca sadece kaydirilarak BitBlt'leniyor (duz bellek kopyasi). Pencere
///     arka plani ayrica boyanmiyor (OnPaintBackground bos): kopya butun pencereyi zaten kapliyor. Yeniden ciziliyor: harita bitmap'i degisince (RebuildMap), zoom / pencere boyutu
///     degisince, ya da gorus onbellegin kenarina tasinca. Kaydirma tam piksele yuvarlaniyor: en fazla yarim piksel,
///     en yakin komsu ornekleme zaten her kenari piksele oturtuyordu.
/// </summary>
internal sealed partial class OverlayForm
{
    private Bitmap? terrainCache;
    private Bitmap? terrainCacheSource;
    private float cacheM11, cacheM12, cacheM21, cacheM22;
    private Size cacheClient;
    private Size cacheSize;
    private PointF cacheShift;
    private int terrainCacheBuilds;
    private readonly PaintProfile profile = new();

    /// <summary>Bu cizimde zemin bastirildi mi: bastirildiysa butun pencere onbellekten kopyalandi (SourceCopy).</summary>
    private bool terrainDrawn;

    /// <summary>
    ///     Arka plan (seffaf anahtar siyahi) burada boyanmiyor: harita cizilen karede zemin onbellegi butun pencereyi
    ///     zaten kopyaliyor. Olcum 21.11.2026: bu doldurma kare basina ~3 ms'ydi, karenin en pahali parcasi. Harita
    ///     cizilmeyen karede OnPaint siyahi kendisi boyuyor (<see cref="ClearIfNoTerrain"/>).
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    /// <summary>PaintOverlay'den sonra: zemin cizilmediyse (harita kapali / henuz yok) pencere siyaha boyanir. O durumda
    ///     PaintOverlay hicbir sey cizmeden donmus oluyor, yani silinen bir sey yok.</summary>
    private void ClearIfNoTerrain(PaintEventArgs e)
    {
        if (!this.terrainDrawn) { e.Graphics.Clear(this.BackColor); }
    }

    /// <summary>Kare adiminda: cizim bolumlerinin 30 sn'lik ozeti gunluge (PaintProfile).</summary>
    private void FlushPaintProfile()
    {
        if (this.profile.Summary(this.terrainCacheBuilds) is not { } line) { return; }

        this.terrainCacheBuilds = 0;
        CrashLog.Write(line, null);
    }

    /// <summary>Haritayi <paramref name="m"/> (hucre -> ekran) ile cizer, onbellekten.</summary>
    private void DrawTerrain(Graphics g, Matrix m)
    {
        var map = this.map;
        if (map is null) { return; }

        var el = m.Elements;                                          // m11 m12 m21 m22 dx dy
        var client = this.ClientSize;
        if (client.Width <= 0 || client.Height <= 0) { return; }

        var x = 0;
        var y = 0;
        var fits = this.terrainHbmp != IntPtr.Zero && ReferenceEquals(this.terrainCacheSource, map) && client == this.cacheClient
                   && el[0] == this.cacheM11 && el[1] == this.cacheM12 && el[2] == this.cacheM21 && el[3] == this.cacheM22;
        if (fits)
        {
            // ekran = onbellek pikseli - cacheShift + bu karenin kaydirmasi
            x = (int)MathF.Round(el[4] - this.cacheShift.X);
            y = (int)MathF.Round(el[5] - this.cacheShift.Y);
            fits = x <= 0 && y <= 0 && x + this.cacheSize.Width >= client.Width && y + this.cacheSize.Height >= client.Height;
        }

        if (!fits)
        {
            (x, y) = this.BuildTerrainCache(map, el, client);
        }

        // Butun pencere kopyalaniyor, karistirma yok: onbellegin GDI kopyasi siyahin ustune hazir karistirilmis (bos yer
        // siyah = seffaf anahtar), BitBlt duz bellek kopyasi. GDI+ DrawImage ayni isi 2-4 ms'de yapiyordu (21.11.2026).
        // Arka plan ayrica boyanmiyor.
        var hdc = g.GetHdc();
        try
        {
            BitBlt(hdc, 0, 0, client.Width, client.Height, this.terrainDc, -x, -y, SrcCopy);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }

        this.terrainDrawn = true;
    }

    private const int SrcCopy = 0x00CC0020;

    // Onbellegin GDI kopyasi: bir bellek DC'sine secili, her karede oradan BitBlt. Onbellek yeniden cizilince yenilenir.
    private IntPtr terrainDc;
    private IntPtr terrainHbmp;
    private IntPtr terrainDcStock;

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    /// <summary>GDI+ onbellegini siyahin ustune karistirip GDI bitmap'ine cevirir ve bellek DC'sine secer.</summary>
    private void SelectTerrainHbitmap()
    {
        var fresh = this.terrainCache!.GetHbitmap(Color.Black);
        if (this.terrainDc == IntPtr.Zero)
        {
            this.terrainDc = CreateCompatibleDC(IntPtr.Zero);
            this.terrainDcStock = SelectObject(this.terrainDc, fresh);
            this.Disposed += (_, _) => this.FreeTerrainGdi();
        }
        else
        {
            SelectObject(this.terrainDc, fresh);
        }

        if (this.terrainHbmp != IntPtr.Zero) { DeleteObject(this.terrainHbmp); }
        this.terrainHbmp = fresh;
    }

    private void FreeTerrainGdi()
    {
        if (this.terrainDc != IntPtr.Zero)
        {
            SelectObject(this.terrainDc, this.terrainDcStock);
            DeleteDC(this.terrainDc);
            this.terrainDc = IntPtr.Zero;
        }

        if (this.terrainHbmp != IntPtr.Zero)
        {
            DeleteObject(this.terrainHbmp);
            this.terrainHbmp = IntPtr.Zero;
        }
    }

    private (int X, int Y) BuildTerrainCache(Bitmap map, float[] el, Size client)
    {
        var marginX = client.Width / 4;
        var marginY = client.Height / 4;
        var width = client.Width + 2 * marginX;
        var height = client.Height + 2 * marginY;

        // GDI+ bitmap'i yalnizca bu cizim icin: GDI kopyasi alininca birakiliyor (pencere 2560x1440'ta ~33 MB).
        this.terrainCache = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        this.cacheSize = new Size(width, height);
        this.cacheShift = new PointF(el[4] + marginX, el[5] + marginY);
        using (var cg = Graphics.FromImage(this.terrainCache))
        {
            cg.Clear(Color.Transparent);
            cg.SmoothingMode = SmoothingMode.None;
            cg.InterpolationMode = InterpolationMode.NearestNeighbor;
            cg.PixelOffsetMode = PixelOffsetMode.Half;
            // Seffaf zemine "ustune" cizmek renk ve alfa'yi oldugu gibi birakir; sonra siyah zemine kopyalaninca eski
            // dogrudan cizimle ayni renk cikar (alfa 235 cizgi, siyahin ustunde).
            cg.CompositingMode = CompositingMode.SourceOver;
            using var t = new Matrix(el[0], el[1], el[2], el[3], this.cacheShift.X, this.cacheShift.Y);
            cg.Transform = t;
            cg.DrawImage(map, 0, 0, this.gridWidth, this.gridHeight);
        }

        this.SelectTerrainHbitmap();
        this.terrainCache.Dispose();
        this.terrainCache = null;
        this.terrainCacheSource = map;
        (this.cacheM11, this.cacheM12, this.cacheM21, this.cacheM22) = (el[0], el[1], el[2], el[3]);
        this.cacheClient = client;
        this.terrainCacheBuilds++;
        return (-marginX, -marginY);
    }
}
