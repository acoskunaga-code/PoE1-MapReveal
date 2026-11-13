namespace Poe2Map;

/// <summary>
///     Ayar penceresinin "Icons" sekmesi: her kategori icin bir baslik (ok + ad + sayi). Basliga tiklayinca bolum
///     asagi dogru acilir ve simgeler siralanir; tekrar tiklayinca gizlenir. Her satirda onizleme, ad ve bir cap
///     cetveli var (0 = kapali, 8 pikselde bir tik). Degisiklik <see cref="OverlaySettings.IconSizes"/>'a yaziliyor.
///
///     Arama (IconsPanel.Search.cs): kutuya yazilinca kategoriler gizlenip butun kategorilerden eslesen simgeler
///     kategori ara basliklariyla tek listede gosteriliyor. Satir uretimi ve ayni simgenin iki satirinin
///     (kategori + arama sonucu) birbirini izlemesi IconsPanel.Rows.cs'te.
///
///     Satirlar bolum ilk acildiginda uretiliyor: yuzlerce cetveli bastan olusturmak pencerenin acilisini
///     yavaslatirdi. Bolumler tek bir tuvalin icinde elle diziliyor; kaydirma kenarlik paneline ait, boylece
///     kaydirma konumu yerlesimi karistirmiyor.
/// </summary>
internal sealed partial class IconsPanel : Panel
{
    private const int HeaderHeight = 28;
    private const int RowHeight = 40;
    private const int TickStep = 8;

    private readonly OverlaySettings settings;
    private readonly IconCatalog catalog;
    private readonly Action changed;
    private readonly Panel canvas = new();
    private readonly List<Section> sections = new();

    internal IconsPanel(OverlaySettings settings, IconCatalog catalog, Action changed)
    {
        this.settings = settings;
        this.catalog = catalog;
        this.changed = changed;

        this.AutoScroll = true;
        this.Controls.Add(this.canvas);

        foreach (var category in catalog.Categories)
        {
            var header = new CategoryHeader(category.Name, category.Entries.Count);
            var section = new Section(category, header);
            header.Click += (_, _) => this.Toggle(section);
            this.canvas.Controls.Add(header);
            this.sections.Add(section);
        }

        this.Relayout();
        this.Resize += (_, _) => this.Relayout();
    }

    /// <summary>Listeyi kaydirir (piksel); cetvellerin fare tekerlegi buraya geliyor.</summary>
    internal void ScrollBy(int delta)
    {
        var current = -this.AutoScrollPosition.Y;
        this.AutoScrollPosition = new Point(0, Math.Max(0, current + delta));
    }

    private void Toggle(Section section)
    {
        section.Header.Open = !section.Header.Open;
        if (section.Header.Open && section.Body is null)
        {
            section.Body = this.BuildRows(new[] { ((string?)null, section.Category.Entries) }, out var height);
            section.BodyHeight = height;
            this.canvas.Controls.Add(section.Body);
        }

        section.Header.Invalidate();
        this.Relayout();
    }

    private void Relayout()
    {
        var width = Math.Max(120, this.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        var y = 0;
        var searching = this.results is not null;

        this.canvas.SuspendLayout();
        foreach (var section in this.sections)
        {
            section.Header.Visible = !searching;
            if (section.Body is { } hiddenBody) { hiddenBody.Visible = !searching && section.Header.Open; }
            if (searching) { continue; }

            section.Header.SetBounds(0, y, width, HeaderHeight);
            y += HeaderHeight;

            if (section.Header.Open && section.Body is { } body)
            {
                body.SetBounds(0, y, width, section.BodyHeight);
                y += section.BodyHeight;
            }
        }

        if (this.results is { } found)
        {
            found.SetBounds(0, 0, width, this.resultsHeight);
            y = this.resultsHeight;
        }

        this.canvas.Size = new Size(width, y);
        this.canvas.ResumeLayout(false);
    }

    private sealed class Section
    {
        internal Section(IconCatalog.Category category, CategoryHeader header)
        {
            this.Category = category;
            this.Header = header;
        }

        internal IconCatalog.Category Category { get; }

        internal CategoryHeader Header { get; }

        /// <summary>Satirlar; bolum ilk acilana kadar yok.</summary>
        internal Panel? Body { get; set; }

        internal int BodyHeight { get; set; }
    }
}
