namespace Poe2Map;

/// <summary>
///     Simge satirlari: onizleme, ad, cap cetveli, deger. Ayni simge iki yerde satir olabiliyor (acik kategori ve
///     arama sonucu); biri oynatilinca oteki de ayni degere gelsin diye cetveller ada gore kayitli.
/// </summary>
internal sealed partial class IconsPanel
{
    private const int GroupHeight = 26;

    private readonly Dictionary<string, List<(RulerBar Ruler, Label Value)>> rulers = new(StringComparer.OrdinalIgnoreCase);
    private bool syncing;

    private static string SizeText(int size) => size == 0 ? "off" : size + " px";

    /// <summary>
    ///     Gruplarin satirlarini tek panelde dizer. Grup basligi null ise baslik yok (acik kategori); varsa ince bir
    ///     ara baslik (arama sonucu: kategori adi ve eslesen sayisi).
    /// </summary>
    private Panel BuildRows(IEnumerable<(string? Title, IReadOnlyList<IconCatalog.Entry> Entries)> groups, out int height)
    {
        var body = new Panel { BackColor = SystemColors.Window };
        body.SuspendLayout();

        var top = 0;
        foreach (var (title, entries) in groups)
        {
            if (title is not null)
            {
                var label = new Label
                {
                    Text = title,
                    Font = new Font(this.Font, FontStyle.Bold),
                    ForeColor = SystemColors.GrayText,
                    TextAlign = ContentAlignment.BottomLeft,
                };
                label.SetBounds(8, top, 440, GroupHeight);
                body.Controls.Add(label);
                top += GroupHeight;
            }

            foreach (var entry in entries)
            {
                this.AddRow(body, entry, top);
                top += RowHeight;
            }
        }

        body.ResumeLayout(false);
        height = top;
        return body;
    }

    private void AddRow(Panel body, IconCatalog.Entry entry, int top)
    {
        // Simgeler koyu zemin icin cizilmis: onizleme koyu bir kutuda.
        var picture = new PictureBox
        {
            Image = this.catalog.Thumbnail(entry),
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = Color.FromArgb(28, 28, 34),
        };
        picture.SetBounds(8, top + 4, IconCatalog.ThumbSize, IconCatalog.ThumbSize);

        var name = new Label { Text = entry.Label, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        name.SetBounds(48, top + 4, 160, IconCatalog.ThumbSize);

        var initial = Math.Clamp(this.settings.IconSizes.GetValueOrDefault(entry.Name), 0, OverlaySettings.IconMaxSize);
        var ruler = new RulerBar(this.ScrollBy)
        {
            Minimum = 0,
            Maximum = OverlaySettings.IconMaxSize,
            TickFrequency = TickStep,
            SmallChange = 1,
            LargeChange = TickStep,
            TickStyle = TickStyle.BottomRight,
            AutoSize = false,
            Value = initial,
        };
        ruler.SetBounds(212, top + 2, 190, 36);

        var value = new Label { Text = SizeText(initial), TextAlign = ContentAlignment.MiddleLeft };
        value.SetBounds(406, top + 4, 56, IconCatalog.ThumbSize);

        ruler.ValueChanged += (_, _) =>
        {
            value.Text = SizeText(ruler.Value);
            if (!this.syncing) { this.SetSize(entry.Name, ruler.Value); }
        };

        if (!this.rulers.TryGetValue(entry.Name, out var list)) { this.rulers[entry.Name] = list = new(); }
        list.Add((ruler, value));

        body.Controls.AddRange(new Control[] { picture, name, ruler, value });
    }

    private void SetSize(string name, int size)
    {
        if (size > 0) { this.settings.IconSizes[name] = size; }
        else { this.settings.IconSizes.Remove(name); }

        // Ayni simgenin oteki satirlari (kategori / arama sonucu) da ayni degere.
        this.syncing = true;
        try
        {
            foreach (var (ruler, value) in this.rulers.GetValueOrDefault(name) ?? new())
            {
                if (ruler.Value == size) { continue; }

                ruler.Value = size;
                value.Text = SizeText(size);
            }
        }
        finally
        {
            this.syncing = false;
        }

        this.changed();
    }

    /// <summary>Atilan bir panelin cetvellerini kayittan cikarir (arama sonucu her aramada yeniden kuruluyor).</summary>
    private void Forget(Panel body)
    {
        foreach (var list in this.rulers.Values)
        {
            list.RemoveAll(r => r.Ruler.Parent == body);
        }
    }
}
