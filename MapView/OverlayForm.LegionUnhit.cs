using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     Legion'da vurulmamis canavarlar (22.11.2026, kullanici: "1 tane bile monster aktif edilmemisse surenin bitmesini
///     bekliyoruz ... bana aktif etmedigimi gosterebilirmisin"). Legion'a tiklayinca ordu donmus halde beliriyor; hepsi
///     vurulursa sure beklenmeden canlaniyorlar. Henuz vurulmamis her canavar (HiddenMonsters.IsUnhitLegion: yalniz
///     frozen_in_time) nadirlik noktasi yerine pembe halkali isaretle cizilir; vurulunca normal noktasina doner.
///     Uyuyan listedekiler de toplanir: ordunun kenarindaki canavar ag balonunun disinda kalabiliyor.
/// </summary>
internal sealed partial class OverlayForm
{
    /// <summary>Pembe isaretin varsayilan capi (piksel); ayar panelinde "Legion" satiri (MonsterPanel).</summary>
    internal const int DefaultLegionSize = 14;

    private static readonly SolidBrush UnhitFill = new(Color.FromArgb(255, 40, 200));

    private ulong[] trackedUnhit = Array.Empty<ulong>();
    private string unhitLogged = "";
    private float unhitDiameter = DefaultLegionSize;

    /// <summary>Ayar paneli etiketini boyamak icin.</summary>
    internal static Color UnhitColor => UnhitFill.Color;

    // Kapaliyken vurulmamis canavarlar da normal nadirlik noktasiyla cizilir.
    private volatile bool unhitOn = true;

    /// <summary>
    ///     Ayar panelindeki "Legion" kutusu ve boyutu (kullanici 22.11.2026: "pembe noktalarin buyuklugunu
    ///     ayarlayabiliyormuyum", "kutuda yap isteyen acmasin").
    /// </summary>
    internal void SetLegionStyle(bool on, int size)
    {
        this.unhitDiameter = Math.Clamp(size, MinIconSize, MaxIconSize);
        this.unhitOn = on;
        if (!on) { this.trackedUnhit = Array.Empty<ulong>(); }
    }

    /// <summary>Onbellek kilidi altinda, RefreshMonsters'tan: vurulmamis Legion canavarinin Render adresi listeye.</summary>
    private static bool CollectUnhit(IntPtr h, ulong entity, EntityReader.EntityInfo info, HashSet<ulong> found)
    {
        if (!HiddenMonsters.IsUnhitLegion(h, info) || !EntityReader.TryReadMonster(h, entity, info, out _)) { return false; }

        if (info.Components.TryGetValue("Render", out var render)) { found.Add(render); }
        return true;
    }

    /// <summary>
    ///     RefreshMonsters'tan. Gunluge (ekrana bakmadan dogrulamak icin) sayi degisince bir satir: 10 ve alti tam, ustu
    ///     25'lik adimlarla (bir Legion'da yuzlerce satir olmasin).
    /// </summary>
    private void SetUnhit(HashSet<ulong> found, int sleeping)
    {
        this.trackedUnhit = found.ToArray();
        var count = found.Count;
        var key = count <= 10 ? $"{count} (uyuyan {sleeping})" : $"{count / 25 * 25}+";
        if (key == this.unhitLogged) { return; }

        this.unhitLogged = key;
        CrashLog.Write($"legion vurulmamis: {count} (uyuyan {sleeping})", null);
    }

    private void PaintUnhitLegion(PaintEventArgs e, Matrix transform) => this.PaintRings(e, transform, this.trackedUnhit, UnhitFill, this.unhitDiameter);
}
