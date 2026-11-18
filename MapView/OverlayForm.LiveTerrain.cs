namespace Poe2Map;

/// <summary>
///     Canli zemin: oyun, alan icindeki engeller acilip kapandiginda zemin izgarasini kendisi guncelliyor.
///     17.11.2026 canli olcum (Dunes haritasi, Hive Fortress kulesi, `MapScan blockages izle`): BreachTower kolunun
///     TriggerableBlockage +0x30 bayti 01->00 dondugu anda cevresinde 456 zemin hucresi degisti. Overlay zemini yalnizca
///     alana girerken okudugu icin yol, girerken acik olan ama sonra kapanan kollardan geciyordu.
///
///     Artik her yol turunda (5 sn) zemin yeniden okunuyor; degismisse maliyet izgarasi ve butun yollar guncel zeminden.
///     Duvar cizimi (arayuz is parcaciginda ~45-60 ms) en fazla 15 saniyede bir: Hive haritasinda zemin her turda degisiyor
///     (27 dalgalik savasta 30 sn'de 5-6 kez) ve her turda yeniden cizmek overlay'i 5 saniyede bir ~90 ms takiltiyordu.
/// </summary>
internal sealed partial class OverlayForm
{
    // Bundan fazlasi degistiyse ayni alan degil (alan bosaltiliyor ya da yeniden kuruluyor): alan degisimi kendisi halleder.
    private const double MaxChangedShare = 0.25;
    private const long TerrainLogMs = 30_000;
    private const long WallRedrawMs = 15_000;
    private long lastTerrainLog;
    private int terrainChanges;
    private byte[]? routeRaw;
    private bool wallsDirty;
    private long lastWallRedraw;

    /// <summary>Alan degisince RefreshExits'ten: onceki alanin zemini yeni alanin yollarina karismasin.</summary>
    private void ResetLiveTerrain()
    {
        this.routeRaw = null;
        this.wallsDirty = false;
    }

    /// <summary>Bellekteki guncel zemin (engel hafizasi uygulanmamis): son turdan beri degistiyse changed = true.</summary>
    private byte[] RouteTerrain(IntPtr h, ulong area, byte[] mapRaw, int bytesPerRow, int gridX, int gridY, out bool changed)
    {
        changed = false;
        var baseline = this.routeRaw is { } r && r.Length == mapRaw.Length ? r : mapRaw;
        if (ChangedTerrain(h, area, baseline, out var bytes) is { } fresh)
        {
            changed = true;
            baseline = fresh;
            this.routeRaw = fresh;
            this.LogTerrainChange(bytes);
        }

        return baseline;
    }

    private void MarkWallsDirty() => this.wallsDirty = true;

    /// <summary>Duvar cizimini yol zeminiyle (engel hafizasi dahil) yeniler, en fazla WallRedrawMs'de bir.</summary>
    private void MaybeRedrawWalls(ulong area, byte[] grid, int bytesPerRow, int gridX, int gridY)
    {
        var now = Environment.TickCount64;
        if (!this.wallsDirty || now - this.lastWallRedraw < WallRedrawMs) { return; }

        this.lastWallRedraw = now;
        this.wallsDirty = false;
        this.RedrawWalls(area, grid, bytesPerRow, gridX, gridY);
    }

    /// <summary>Zemin bellekteki halinden farkliysa guncel kopyasi, degilse (ya da okunamazsa) null.</summary>
    private static byte[]? ChangedTerrain(IntPtr h, ulong area, byte[] current, out int changed)
    {
        changed = 0;
        if (TerrainFinder.TryReadAt(h, area + GameLayout.TerrainInArea) is not { } t || t.DataLength != current.Length)
        {
            return null;
        }

        var fresh = new byte[current.Length];
        if (!EntityReader.Read(h, t.DataStart, fresh) || fresh.AsSpan().SequenceEqual(current)) { return null; }

        for (var i = 0; i < fresh.Length; i++)
        {
            if (fresh[i] != current[i]) { changed++; }
        }

        return changed <= current.Length * MaxChangedShare ? fresh : null;
    }

    private void LogTerrainChange(int changed)
    {
        this.terrainChanges++;
        var now = Environment.TickCount64;
        if (now - this.lastTerrainLog < TerrainLogMs) { return; }

        CrashLog.Write($"zemin degisti (kapi/kol acildi ya da kapandi): {changed} bayt, son {TerrainLogMs / 1000} sn'de " +
                       $"{this.terrainChanges} kez; yollar guncel zeminden, duvarlar en fazla {WallRedrawMs / 1000} sn'de bir", null);
        this.lastTerrainLog = now;
        this.terrainChanges = 0;
    }

    private void RedrawWalls(ulong area, byte[] fresh, int bytesPerRow, int gridX, int gridY)
    {
        if (this.IsDisposed || !this.IsHandleCreated) { return; }
        try
        {
            this.BeginInvoke(() =>
            {
                if (this.areaBase == area && this.gridWidth == gridX && this.gridHeight == gridY)
                {
                    this.SetMap(fresh, bytesPerRow, gridX, gridY);
                }
            });
        }
        catch (InvalidOperationException) { }
    }
}
