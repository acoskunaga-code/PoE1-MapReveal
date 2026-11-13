using System.Diagnostics;

namespace Poe2Map;

/// <summary>
///     Alan degisiminde yeni alani BELLEK TARAMADAN bulur.
///
///     Eskiden her alan degisiminde butun bellek (7-13 GB) taraniyordu: 4-7 saniye. Oyuncu
///     varligi icinde bulundugu alani + 0x78'de tutuyor; yeni alan oradan, zemini + GameLayout.TerrainInArea'dan
///     okunuyor ve sinaniyor (LocalPlayer.FromEntity).
///
///     Oyuncu varligi alan degisiminde yeniden olusturulmus olabilir. O durumda bilinen
///     alan yapilarindan (eskiler dahil) + 0x5D0 ile guncel oyuncuya ulasiliyor.
/// </summary>
internal sealed partial class OverlayContext
{
    /// <summary>Yukleme ekrani bu kadar surerse taramasiz yoldan vazgecip taramaya gec.</summary>
    private static readonly TimeSpan FastLocateWait = TimeSpan.FromSeconds(8);

    /// <summary>Alan degisiminden haritanin yuklenmesine kadar gecen sure - gunluk icin.</summary>
    private readonly Stopwatch locateClock = new();

    private LocalPlayer.Found? FastLocate(IntPtr h)
    {
        List<ulong> areas;
        lock (this.knownAreas) { areas = new List<ulong>(this.knownAreas); }

        var entity = LocalPlayer.Current?.Entity ?? 0;
        if (entity == 0 && areas.Count == 0 && !AreaHolder.Ready) { return null; }

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < FastLocateWait)
        {
            // Birincil yol (PoE1): kalici nesnelerin gosterdigi alan. Eski iki yol PoE2'den kaldi ve PoE1'de tutmuyor
            // (oyuncu varligi her alanda yeniden olusuyor, eski alan yapilari bosaliyor); ikisi de yedek.
            var held = AreaHolder.Current(h);
            var found = (held != 0 ? LocalPlayer.FromArea(h, held) : null) ??
                        (entity != 0 ? LocalPlayer.FromEntity(h, entity) : null) ??
                        LocalPlayer.FromKnownAreas(h, areas);
            if (found is not null) { return found; }

            Thread.Sleep(100);
        }

        return null;
    }

    private static void AddKnown(List<ulong> areas, ulong area)
    {
        if (!areas.Contains(area)) { areas.Add(area); }

        // Eski alan yapilari bir sure sonra bosaltiliyor; liste sonsuz buyumesin.
        if (areas.Count > 32) { areas.RemoveAt(0); }
    }
}
