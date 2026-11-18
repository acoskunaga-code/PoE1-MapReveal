namespace Poe2Map;

/// <summary>
///     Kapali lig engellerinin hafizasi (Breach kulesinin kollari, BrequelBlocker duvarlari).
///
///     17.11.2026 canli (Dunes, Hive Fortress, 27 dalgalik savas; `MapScan entsnap` 8 sn arayla, 21 an): oyun kapali bir
///     engeli zemin izgarasina YALNIZCA varlik oyuncunun ag balonundayken yaziyor. Kol listeye girdigi anda cevresinde
///     (en fazla 33 hucre) ~160 hucre 0 oluyor, listeden ciktigi anda ayni hucreler yeniden aciliyor; +0x30 hic donmedi.
///     Yani uzaktaki kapali kollar istemcinin zemininde acik gorunuyor ve yol onlarin icinden geciyordu
///     (kullanici: "27/27 olunca sadece 1 yoldan cikilabiliyor, diger yollar duvarla kapaniyor").
///
///     Burada: kapali gorulen her engelin cevresindeki kapali hucreler (BarrierRadius icinde, o anki zeminde 0 olanlar)
///     kaydediliyor ve engel balondan ciksa da yol hesabinda kapali tutuluyor. Acik (00) gorulen engel hafizadan siliniyor.
///     Duvar cizimi de ayni hafizayla: kapali kollar uzaktayken de duvar olarak gorunuyor.
/// </summary>
internal sealed partial class OverlayForm
{
    // Olculen en uzak degisen hucre 33; bir hucre pay.
    private const int BarrierRadius = 34;

    private readonly Dictionary<uint, int[]> closedBarriers = new();

    /// <summary>Alan degisince RefreshExits'ten.</summary>
    private void ResetBarriers() => this.closedBarriers.Clear();

    /// <summary>Gorulen engellerle hafizayi gunceller; kapali engel kumesi degistiyse true.</summary>
    private bool UpdateBarriers(List<ExitFinder.Barrier> seen, byte[] live, int bytesPerRow, int gridX, int gridY)
    {
        var changed = false;
        foreach (var b in seen)
        {
            if (!b.Closed)
            {
                changed |= this.closedBarriers.Remove(b.Id);
                continue;
            }

            changed |= !this.closedBarriers.ContainsKey(b.Id);
            this.closedBarriers[b.Id] = ClosedCellsAround(live, bytesPerRow, gridX, gridY, b.X, b.Y);
        }

        return changed;
    }

    /// <summary>Hafizadaki kapali hucreler uygulanmis zemin; hafiza bossa ayni dizi.</summary>
    private byte[] WithBarriers(byte[] live, int bytesPerRow, int gridX)
    {
        if (this.closedBarriers.Count == 0) { return live; }

        var grid = (byte[])live.Clone();
        foreach (var cells in this.closedBarriers.Values)
        {
            foreach (var c in cells)
            {
                var x = c % gridX;
                var i = ((long)(c / gridX) * bytesPerRow) + (x / 2);
                if (i < grid.Length) { grid[i] &= (x & 1) == 0 ? (byte)0xF0 : (byte)0x0F; }
            }
        }

        return grid;
    }

    private static int[] ClosedCellsAround(byte[] live, int bytesPerRow, int gridX, int gridY, float worldX, float worldY)
    {
        var cx = (int)(worldX / PlayerChain.WorldPerCell);
        var cy = (int)(worldY / PlayerChain.WorldPerCell);
        var cells = new List<int>();
        for (var y = Math.Max(0, cy - BarrierRadius); y <= Math.Min(gridY - 1, cy + BarrierRadius); y++)
        {
            for (var x = Math.Max(0, cx - BarrierRadius); x <= Math.Min(gridX - 1, cx + BarrierRadius); x++)
            {
                if (((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) > BarrierRadius * BarrierRadius) { continue; }
                var i = ((long)y * bytesPerRow) + (x / 2);
                if (i >= live.Length) { continue; }
                var v = (x & 1) == 0 ? live[i] & 0x0F : live[i] >> 4;
                if (v == 0) { cells.Add((y * gridX) + x); }
            }
        }

        return cells.ToArray();
    }
}
