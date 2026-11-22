using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     Aranan canavarlar (21.11.2026, kullanici: "Fungal Shamans bu isimdeki rare monsterlari tespit etmelisin"):
///     oyundaki adi (Render +0x148) listedekilerden biri olan canli dusman canavar, nadirlik noktasi yerine kendi
///     isaretiyle cizilir: yesil dolgu, beyaz halka, disinda koyu halka. Canlida (MapScan rarewatch) "Fungal Shaman" =
///     Metadata/Monsters/BeehiveBehemoth/BeehiveBehemothBossStandaloneAllFlame, rare.
///
///     Uyuyan listedekiler de cizilir: oyun bu canavarlari ag balonundan cikinca uyuyan listeye yeni bir kayitla
///     koyuyor ve konum son bilinen yer (henuz dovusulmemis canavarda dogdugu yer). Olunce can 0, TryReadMonster eler.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly string[] SpecialMonsterNames = { "Fungal Shaman" };

    private static readonly SolidBrush SpecialFill = new(Color.FromArgb(60, 255, 60));
    private static readonly Pen SpecialRing = new(Color.FromArgb(255, 255, 255), 2f);
    private static readonly Pen SpecialOuter = new(Color.FromArgb(20, 20, 20), 2f);

    private ulong[] trackedSpecial = Array.Empty<ulong>();
    private int specialLogged = -1;

    /// <summary>RefreshMonsters'tan: aranan canavar sayisi degisince gunluge bir satir (ekrana bakmadan dogrulamak icin).</summary>
    private void SetSpecial(HashSet<ulong> found)
    {
        this.trackedSpecial = found.ToArray();
        if (found.Count == this.specialLogged) { return; }

        this.specialLogged = found.Count;
        CrashLog.Write($"aranan canavar ({string.Join(", ", SpecialMonsterNames)}): {found.Count}", null);
    }

    /// <summary>Adi listede mi; ad varlik bilgisinde onbellekte (varlik basina bir okuma).</summary>
    private static bool IsSpecialMonster(IntPtr h, EntityReader.EntityInfo info)
    {
        if (!info.IsMonster) { return false; }

        var name = MapIconReader.DisplayName(h, info);
        return name.Length > 0 && Array.Exists(SpecialMonsterNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Onbellek kilidi altinda, RefreshMonsters'tan: aranan canli canavarin Render adresi listeye.</summary>
    private static bool CollectSpecial(IntPtr h, ulong entity, EntityReader.EntityInfo info, HashSet<ulong> found)
    {
        if (!IsSpecialMonster(h, info) || !EntityReader.TryReadMonster(h, entity, info, out _)) { return false; }

        if (info.Components.TryGetValue("Render", out var render)) { found.Add(render); }
        return true;
    }

    private void PaintSpecialMonsters(PaintEventArgs e, Matrix transform) =>
        this.PaintRings(e, transform, this.trackedSpecial, SpecialFill, Math.Max(14f, this.icons.Diameter[2] * 1.6f));

    /// <summary>
    ///     Render adreslerinin canli konumuna <paramref name="d"/> capinda halkali isaret: koyu dis halka, dolgu, beyaz
    ///     halka (Special, LegionUnhit).
    /// </summary>
    private void PaintRings(PaintEventArgs e, Matrix transform, ulong[] list, Brush fill, float d)
    {
        var h = this.handle;
        if (list.Length == 0 || h == IntPtr.Zero) { return; }

        var points = new List<PointF>(list.Length);
        foreach (var render in list)
        {
            if (!EntityReader.Read(h, render + EntityReader.RenderPosition, this.monsterPosition)) { continue; }

            var x = BitConverter.ToSingle(this.monsterPosition, 0);
            var y = BitConverter.ToSingle(this.monsterPosition, 4);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0) { continue; }

            points.Add(new PointF((x / PlayerPos.UnitsPerCell) + this.MapOffsetX, (y / PlayerPos.UnitsPerCell) + this.MapOffsetY));
        }

        if (points.Count == 0) { return; }

        var array = points.ToArray();
        transform.TransformPoints(array);

        var saved = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var p in array)
        {
            if (p.X < -d || p.Y < -d || p.X > this.ClientSize.Width + d || p.Y > this.ClientSize.Height + d) { continue; }

            e.Graphics.DrawEllipse(SpecialOuter, p.X - (d / 2f) - 2f, p.Y - (d / 2f) - 2f, d + 4f, d + 4f);
            e.Graphics.FillEllipse(fill, p.X - (d / 2f), p.Y - (d / 2f), d, d);
            e.Graphics.DrawEllipse(SpecialRing, p.X - (d / 2f), p.Y - (d / 2f), d, d);
        }

        e.Graphics.SmoothingMode = saved;
    }
}
