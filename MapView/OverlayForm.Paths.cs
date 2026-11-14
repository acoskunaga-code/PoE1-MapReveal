using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     Cikis yollari: oyuncudan alanin her cikis kapisina giden yol, her biri ayri renkte,
///     kapinin yaninda gittigi yerin adiyla.
///
///     Cikislar iki kaynaktan (TileExits): zemin karolari alan yuklenir yuklenmez butun
///     cikislari veriyor, uzaktakiler dahil; kapi varliklari yaklasinca yukleniyor ve
///     karo tahmininin yerine kesin konumu veriyor. Karolar alan basina bir kez okunuyor,
///     varliklar bes saniyede bir.
///
///     Her cikisa bir kez mesafe haritasi (FlowField) hesaplaniyor, konumu degisirse
///     (varlik yuklenip karo tahminini duzelttiginde) yeniden. Kapi sayisi degisirse
///     maliyet izgarasi ve butun haritalar yeniden hesaplaniyor.
///
///     Cizimde yol her karede mesafe haritasindan cikariliyor - oyuncu yuruyunce yeniden
///     arama yok. Kapiya varmis sayilacak kadar yakinsan o kapinin yolu cizilmiyor.
/// </summary>
internal sealed partial class OverlayForm
{
    private const int ExitRefreshMs = 5000;

    // Alana girdikten sonra kapi varliklari ve karo tablosu birkac saniye icinde doluyor; ilk 8 turu 1.5 saniyede bir at.
    private const int ExitWarmupMs = 1500;
    private const int ExitWarmupRuns = 8;

    // Karolar alan basina bir kez okunup sonsuza dek saklanirsa, okuma alan yuklenirken yapildiysa (tablo yarim, kimlik
    // henuz okunamiyor) o alanin cikislari bir daha hic gelmiyordu. Bu yuzden ilk dakika her turda yeniden okunuyor
    // (olculen maliyet birkac on ms); sonra hedef bulunamadiysa 15 saniyede bir yeniden deneniyor.
    private const long TileWarmupMs = 60_000;
    private const long TileRetryMs = 15_000;
    private const float PathWidth = 3f;
    private const float ReachedCells = 14f;

    /// <summary>
    ///     Cikis renkleri, bulunma sirasina gore. Canavar renkleriyle (kirmizi, mavi, sari,
    ///     kahverengi) karismayacak sekilde secildi.
    /// </summary>
    private static readonly Color[] ExitColors =
    {
        Color.FromArgb(0, 235, 235),     // camgobegi
        Color.FromArgb(235, 70, 235),    // eflatun
        Color.FromArgb(120, 255, 60),    // yesil
        Color.FromArgb(255, 150, 30),    // turuncu
        Color.FromArgb(245, 245, 245),   // beyaz
        Color.FromArgb(255, 130, 190),   // pembe
    };

    private static readonly Font ExitLabelFont = new("Segoe UI", 9f, FontStyle.Bold);
    private static readonly SolidBrush LabelShadow = new(Color.FromArgb(20, 20, 20));

    private sealed class PathTarget
    {
        internal required TileExits.Target Exit;
        internal required FlowField Field;
        internal required Pen Pen;
        internal required SolidBrush Brush;
        internal int LastCell = -1;
        internal PointF[] LastPoints = Array.Empty<PointF>();
        internal bool NoPathLogged;
    }

    private readonly Dictionary<(uint, ulong), EntityReader.EntityInfo?> exitCache = new();
    private readonly Dictionary<string, Color> exitColors = new(StringComparer.Ordinal);
    private System.Threading.Timer? pathTimer;
    private PathTarget[] pathTargets = Array.Empty<PathTarget>();
    private const bool TileGuessedExits = true;
    private List<TileExits.Target>? tileTargets;
    private ulong pathArea;
    private byte[]? costGrid;
    private int costWidth;
    private int costHeight;
    private int doorCount = -1;
    private int pathRefreshing;
    private const long PaintLogMs = 10_000;
    private long lastPaintLog;
    private string lastPaintSummary = "";
    private int exitRuns;
    private long areaSince;
    private long lastTileRead;
    private string? areaId;
    private string lastExitReport = "";

    /// <summary>SetArea'dan cagriliyor; zamanlayiciyi bir kez kurar.</summary>
    private void StartPathTracking()
    {
        if (this.pathTimer is not null) { return; }

        this.pathTimer = new System.Threading.Timer(_ => this.RefreshExits(), null, 500, ExitWarmupMs);
        this.Disposed += (_, _) => this.pathTimer?.Dispose();
    }

    private void RefreshExits()
    {
        if (Interlocked.Exchange(ref this.pathRefreshing, 1) == 1) { return; }

        try
        {
            var h = this.handle;
            var area = this.areaBase;
            var raw = this.raw;
            var bytesPerRow = this.rawBytesPerRow;
            var gridX = this.gridWidth;
            var gridY = this.gridHeight;

            if (h == IntPtr.Zero || area == 0 || raw is null || gridX <= 0 || gridY <= 0)
            {
                this.pathTargets = Array.Empty<PathTarget>();
                return;
            }

            if (area != this.pathArea)
            {
                this.exitCache.Clear();
                this.exitColors.Clear();
                this.pathTargets = Array.Empty<PathTarget>();
                this.tileTargets = null;
                this.costGrid = null;
                this.doorCount = -1;
                this.pathArea = area;
                this.areaSince = Environment.TickCount64;
                this.lastTileRead = 0;
                this.areaId = null;
                this.lastExitReport = "";
            }

            // Uzaktaki cikislar varlik olarak yuklenmedigi icin karolardan bulunuyor (TileExits.FromTiles: once liste,
            // sonra kurallar; yakindaki cikislar AreaTransition bilesenli varliklardan geliyor ve adi oyundan okunuyor).
            // Karolar degismez ama OKUMA gecikebilir: alan yuklenirken tablo yarim ya da kimlik okunamaz olabilir.
            var now = Environment.TickCount64;
            var warm = now - this.areaSince < TileWarmupMs;
            if (this.tileTargets is null ||
                (warm && now - this.lastTileRead >= ExitWarmupMs) ||
                (this.tileTargets.Count == 0 && now - this.lastTileRead >= TileRetryMs))
            {
                this.lastTileRead = now;

                // Alan kimligi (2_7_11): kampanya disinda null kalir; kampanyada okunana kadar her turda yeniden denenir.
                this.areaId ??= AreaIdentity.RadarId(h, area);

                var tiles = TileGuessedExits
                    ? TileReader.Read(h, area + GameLayout.TerrainInArea, gridX / TileReader.CellsPerTile, gridY / TileReader.CellsPerTile)
                    : new List<TileReader.Tile>();
                var fresh = TileExits.FromTiles(tiles, this.areaId, out var report);

                // Her tur yazmamak icin yalnizca ozet degisince: bir alan icin birkac satir, "yol neden yok" sorusunun cevabi.
                var line = $"cikis ozeti {this.areaId ?? "(kampanya alani degil ya da okunamadi)"}: {tiles.Count} karo, " +
                           $"{tiles.Count(t => t.Detail.Length > 0)} detayli  |  {report}";
                if (line != this.lastExitReport)
                {
                    CrashLog.Write(line, null);
                    this.lastExitReport = line;
                }

                // Gecici bos okuma (tablo yeniden kuruluyor) onceki iyi sonucu silmesin.
                if (tiles.Count > 0 || this.tileTargets is null) { this.tileTargets = fresh; }
            }

            var doors = new List<(float X, float Y)>();
            var entities = ExitFinder.Find(h, area, this.exitCache, doors);
            var exits = TileExits.WithEntities(this.tileTargets, entities);

            var rebuildAll = false;
            if (this.costGrid is null || doors.Count != this.doorCount)
            {
                this.costGrid = FlowField.BuildCostGrid(raw, bytesPerRow, gridX, gridY, doors, out this.costWidth, out this.costHeight);
                this.doorCount = doors.Count;
                rebuildAll = true;
            }

            var previous = this.pathTargets;
            var next = new List<PathTarget>(exits.Count);

            foreach (var exit in exits)
            {
                // Renk anahtara bagli: yeni bir cikis eklenince eskilerin rengi degismesin.
                if (!this.exitColors.TryGetValue(exit.Key, out var color))
                {
                    color = ExitColors[this.exitColors.Count % ExitColors.Length];
                    this.exitColors[exit.Key] = color;
                }

                var reuse = rebuildAll
                    ? null
                    : previous.FirstOrDefault(t => t.Exit.Key == exit.Key && t.Exit.X == exit.X && t.Exit.Y == exit.Y);

                next.Add(reuse ?? new PathTarget
                {
                    Exit = exit,
                    Field = FlowField.Build(this.costGrid, this.costWidth, this.costHeight, exit.X, exit.Y, exit.Region),
                    Pen = new Pen(color, PathWidth) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round },
                    Brush = new SolidBrush(color),
                });
            }

            // Hesap surerken alan degistiyse sonucu yazma.
            if (area == this.areaBase)
            {
                this.pathTargets = next.ToArray();
                this.ReportExitNames(next);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("cikis yollari", ex);
        }
        finally
        {
            Interlocked.Exchange(ref this.pathRefreshing, 0);

            // Isinma turlari bitti: normal 5 saniyelik ritme don.
            if (Interlocked.Increment(ref this.exitRuns) == ExitWarmupRuns)
            {
                this.pathTimer?.Change(ExitRefreshMs, ExitRefreshMs);
            }
        }
    }

    /// <summary>Yeni alan: yollari hemen hesapla ve ilk turlari sik at (SetArea cagiriyor).</summary>
    private void RestartExitWarmup()
    {
        Interlocked.Exchange(ref this.exitRuns, 0);
        this.pathTimer?.Change(50, ExitWarmupMs);
    }

    /// <summary>
    ///     Yol tusu (ayarsiz exe, GlobalHotKey): true iken cikis yollari, kapi isaretleri ve adlari cizilmiyor. Hesap
    ///     arka planda suruyor; tekrar acinca yollar beklemeden geliyor.
    /// </summary>
    internal bool HidePaths;

    // Ayar penceresinin Paths sekmesi: yolu cizilmeyecek cikis adlari. Butun olarak degistiriliyor (cizimle yarismasin).
    private HashSet<string> hiddenPaths = new(StringComparer.OrdinalIgnoreCase);
    private string reportedExitNames = "";

    /// <summary>Gorulen cikis adlari (degisince, arka plan is parcacigindan): ayar listesine eklenmek uzere.</summary>
    internal Action<IReadOnlyList<string>>? ExitNamesSeen;

    /// <summary>Yolu cizilmeyecek cikis adlari; kapinin isareti ve adi yine ciziliyor.</summary>
    internal void SetHiddenPaths(IEnumerable<string> names) =>
        this.hiddenPaths = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private void ReportExitNames(List<PathTarget> targets)
    {
        var names = targets.Select(t => t.Exit.Name).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var key = string.Join("|", names);
        if (key == this.reportedExitNames) { return; }

        this.reportedExitNames = key;
        this.ExitNamesSeen?.Invoke(names);
    }

    /// <summary>Oyuncudan her cikisa yol, cikisin yerine bir isaret ve ad. PaintOverlay'den cagriliyor.</summary>
    private void PaintPaths(PaintEventArgs e, Matrix transform, PlayerPos player)
    {
        var targets = this.pathTargets;
        if (targets.Length == 0 || this.HidePaths) { return; }

        var saved = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.None;

        var playerCell = ((int)(player.Y / PlayerPos.UnitsPerCell) / FlowField.Factor * 100000) +
                         ((int)(player.X / PlayerPos.UnitsPerCell) / FlowField.Factor);

        var hidden = this.hiddenPaths;
        foreach (var target in targets)
        {
            var ex = (target.Exit.X - player.X) / PlayerPos.UnitsPerCell;
            var ey = (target.Exit.Y - player.Y) / PlayerPos.UnitsPerCell;

            // Paths sekmesinde kapatilan cikis: yol yok, isaret ve ad var (asagida).
            var reached = (ex * ex) + (ey * ey) < ReachedCells * ReachedCells || hidden.Contains(target.Exit.Name);

            if (!reached)
            {
                // Oyuncu kaba hucre degistirmediyse yol ayni; mesafe haritasini yeniden yurume.
                if (target.LastCell != playerCell)
                {
                    var path = target.Field.PathFrom(player.X, player.Y);

                    // Yol hic cikmiyorsa nedeni gunlukte gorunsun (kapi ulasilamaz bolgede, oyuncu kapali hucrede...).
                    if (path.Count == 0 && !target.NoPathLogged)
                    {
                        target.NoPathLogged = true;
                        CrashLog.Write($"yol yok: {target.Exit.Name} ({target.Exit.Key}) oyuncu ({player.X:F0}, {player.Y:F0}) kapi ({target.Exit.X:F0}, {target.Exit.Y:F0})", null);
                    }

                    // Mesafe haritasi kapinin cevresini hedef sayiyor (kapi cogu zaman duvarin
                    // icinde); yol bu yuzden kapidan ~18 hucre once bitiyordu. Son parcayi
                    // kapinin kendisine duz cizgiyle bagliyoruz.
                    if (path.Count > 0)
                    {
                        path.Add(new PointF(target.Exit.X / PlayerPos.UnitsPerCell, target.Exit.Y / PlayerPos.UnitsPerCell));
                    }

                    target.LastPoints = path.Select(q => new PointF(q.X + this.MapOffsetX, q.Y + this.MapOffsetY)).ToArray();
                    target.LastCell = playerCell;
                }

                if (target.LastPoints.Length >= 2)
                {
                    var points = (PointF[])target.LastPoints.Clone();
                    transform.TransformPoints(points);
                    e.Graphics.DrawLines(target.Pen, points);
                }
            }

            // Cikisin yeri: kucuk dolu kare ve gittigi yer. Yol cizilmese de gorunsun.
            var marker = new[]
            {
                new PointF((target.Exit.X / PlayerPos.UnitsPerCell) + this.MapOffsetX,
                           (target.Exit.Y / PlayerPos.UnitsPerCell) + this.MapOffsetY),
            };
            transform.TransformPoints(marker);
            var m = marker[0];

            e.Graphics.FillRectangle(target.Brush, m.X - 6, m.Y - 6, 12, 12);
            e.Graphics.DrawRectangle(MonsterOutline, m.X - 6, m.Y - 6, 12, 12);

            // Saydamlik anahtari saf siyah; golge 20'lik gri, yazi okunakli kalsin. Kenar
            // yumusatmasi siyaha karisip bulanik hale getirdigi icin tek bit yazi.
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            e.Graphics.DrawString(target.Exit.Name, ExitLabelFont, LabelShadow, m.X + 10, m.Y - 7);
            e.Graphics.DrawString(target.Exit.Name, ExitLabelFont, target.Brush, m.X + 9, m.Y - 8);
        }

        e.Graphics.SmoothingMode = saved;

        // "Yol cizilmiyor" sikayetinin cevabi gunlukte: cizim gercekten calisti mi, her hedefin yolu kac nokta.
        var tick = Environment.TickCount64;
        if (tick - this.lastPaintLog >= PaintLogMs)
        {
            this.lastPaintLog = tick;
            var summary = "yol cizimi: " + string.Join("; ", targets.Select(t => $"{t.Exit.Name} {(t.LastPoints.Length >= 2 ? "yol var" : "yol yok")}"));
            if (summary != this.lastPaintSummary)
            {
                this.lastPaintSummary = summary;
                CrashLog.Write(summary, null);
            }
        }
    }
}
