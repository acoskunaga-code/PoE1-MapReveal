using System.Drawing.Drawing2D;

namespace Poe2Map;

/// <summary>
///     Abyssal Wasting (AbyssalWasting.cs): yerdeki zemin mor dolgulu daire + "WASTING", zemini birakan canavar (Wasting
///     Well modu) mor halka + "WASTING WELL". Varlik gezintisi canavarlarla ortak (RefreshMonsters, yalnizca uyanik
///     liste: ikisi de canli varlik). Konum her karede okunuyor. Ayar: Altars sekmesi, varsayilan acik.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly SolidBrush WastingFill = new(Color.FromArgb(110, 150, 40, 210));
    private static readonly Pen WastingPen = new(Color.FromArgb(200, 90, 255), 2.5f);
    private static readonly SolidBrush WastingText = new(Color.FromArgb(215, 150, 255));
    private const float WastingGroundRadius = 11f;
    private const float WastingWellRadius = 13f;

    private bool abyssalWasting = true;
    private (ulong Render, bool Ground, string Label)[] trackedWasting = Array.Empty<(ulong, bool, string)>();
    private readonly byte[] wastingPosition = new byte[8];

    /// <summary>Abyssal Wasting isaretlerini ac / kapat (ayar penceresi, Altars sekmesi).</summary>
    internal void SetAbyssalWasting(bool on)
    {
        this.abyssalWasting = on;
        if (!on) { this.trackedWasting = Array.Empty<(ulong, bool, string)>(); }
    }

    /// <summary>Varlik Wasting zemini ya da Wasting Well modlu canavar ise listeye ekler. Onbellek kilidi altinda.</summary>
    private void CollectWasting(IntPtr h, EntityReader.EntityInfo info, List<(ulong, bool, string)> into)
    {
        var ground = AbyssalWasting.IsGround(info);
        if (!ground)
        {
            if (!info.IsMonster) { return; }

            info.WastingWell ??= AbyssalWasting.IsTarhulk(info) ? true : AbyssalWasting.HasWellMod(h, info);
            if (info.WastingWell != true) { return; }
        }

        if (!info.Components.TryGetValue("Render", out var render)) { return; }

        // Zemin canli kaldikca (DiesAfterTime) cizilsin; olen kaynak canavar cizilmesin.
        if (info.Components.TryGetValue("Life", out var life))
        {
            var hp = new byte[8];
            if (EntityReader.Read(h, life + EntityReader.LifeTotal, hp) &&
                BitConverter.ToInt32(hp, 0) > 0 && BitConverter.ToInt32(hp, 4) <= 0) { return; }
        }

        into.Add((render, ground, ground ? "WASTING" : AbyssalWasting.IsTarhulk(info) ? "TARHULK" : "WASTING WELL"));
    }

    private (int Ground, int Well) lastWastingCount;

    /// <summary>Turun sonucu; sayilar degisince gunluge (ekrana bakmadan dogrulamak icin).</summary>
    private void SetWasting(List<(ulong Render, bool Ground, string Label)> found)
    {
        this.trackedWasting = found.ToArray();
        // Zeminler birkac saniyede bir topluca dogup sonuyor (20.11 03:00: 1-2-3-4-3-2-1-0 bes saniyede): her sayi
        // degisimini yazmak gunlugu bogar. Kaynak sayisi da (Tarhulk'lar, 03:02'de 17-25) her saniye oynuyor. Yalnizca
        // zemin var/yok gecisinde.
        var count = (found.Count(f => f.Ground), found.Count(f => !f.Ground));
        var changed = (count.Item1 > 0) != (this.lastWastingCount.Ground > 0);
        var appeared = count.Item1 > 0 && this.lastWastingCount.Ground == 0;
        this.lastWastingCount = count;
        if (!changed) { return; }

        CrashLog.Write($"abyssal wasting: {count.Item1} zemin, {count.Item2} kaynak canavar (Tarhulk / Wasting Well)", null);
        if (appeared) { this.LogWastingSource(found.First(f => f.Ground).Render); }
    }

    /// <summary>
    ///     Tani (20.11.2026): zemin ilk belirdiginde ona en yakin 4 canavar, nadirligi ve ozel modlariyla. Zemin, Wasting
    ///     Well modlu bir canavar yokken de topluca cikiyordu; birakani bulmak icin.
    /// </summary>
    private void LogWastingSource(ulong groundRender)
    {
        var h = this.handle;
        var pos = new byte[8];
        if (h == IntPtr.Zero || !EntityReader.Read(h, groundRender + EntityReader.RenderPosition, pos)) { return; }

        var gx = BitConverter.ToSingle(pos, 0);
        var gy = BitConverter.ToSingle(pos, 4);
        var near = new List<(float Distance, string Text)>();
        lock (this.entityCache)
        {
            foreach (var info in this.entityCache.Values)
            {
                if (info is not { IsMonster: true } || AbyssalWasting.IsGround(info)) { continue; }
                if (!info.Components.TryGetValue("Render", out var render) ||
                    !EntityReader.Read(h, render + EntityReader.RenderPosition, pos)) { continue; }

                var d = MathF.Sqrt(MathF.Pow(BitConverter.ToSingle(pos, 0) - gx, 2) + MathF.Pow(BitConverter.ToSingle(pos, 4) - gy, 2));
                if (!float.IsFinite(d) || d > 600) { continue; }

                var mods = string.Join(",", AbyssalWasting.SpecialMods(h, info));
                near.Add((d, $"{d:F0} {info.Path}{(mods.Length > 0 ? " [" + mods + "]" : "")}"));
            }
        }

        CrashLog.Write("abyssal wasting yakinindakiler: " +
                       string.Join("  |  ", near.OrderBy(n => n.Distance).Take(4).Select(n => n.Text)), null);
    }

    /// <summary>Haritanin donusumuyle zeminler ve kaynak canavarlar; canavar noktalarinin ustune.</summary>
    private void PaintWasting(PaintEventArgs e, Matrix transform)
    {
        var list = this.trackedWasting;
        var h = this.handle;
        if (list.Length == 0 || h == IntPtr.Zero) { return; }

        var points = new PointF[list.Length];
        var valid = new bool[list.Length];
        for (var i = 0; i < list.Length; i++)
        {
            if (!EntityReader.Read(h, list[i].Render + EntityReader.RenderPosition, this.wastingPosition)) { continue; }

            var x = BitConverter.ToSingle(this.wastingPosition, 0);
            var y = BitConverter.ToSingle(this.wastingPosition, 4);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 0 || y <= 0) { continue; }

            points[i] = new PointF((x / PlayerPos.UnitsPerCell) + this.MapOffsetX, (y / PlayerPos.UnitsPerCell) + this.MapOffsetY);
            valid[i] = true;
        }

        transform.TransformPoints(points);

        var width = this.ClientSize.Width;
        var height = this.ClientSize.Height;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        for (var i = 0; i < list.Length; i++)
        {
            var p = points[i];
            if (!valid[i] || p.X < -30 || p.Y < -30 || p.X > width + 30 || p.Y > height + 30) { continue; }

            var ground = list[i].Ground;
            var r = ground ? WastingGroundRadius : WastingWellRadius;
            if (ground) { e.Graphics.FillEllipse(WastingFill, p.X - r, p.Y - r, r * 2, r * 2); }
            e.Graphics.DrawEllipse(WastingPen, p.X - r, p.Y - r, r * 2, r * 2);

            var label = list[i].Label;
            e.Graphics.DrawString(label, IconLabelFont, LabelShadow, p.X + r + 4, p.Y - 6);
            e.Graphics.DrawString(label, IconLabelFont, WastingText, p.X + r + 3, p.Y - 7);
        }
    }
}
