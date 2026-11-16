namespace Poe2Map;

/// <summary>
///     Ayar penceresinin "Paths" sekmesi: gorulen cikislarin listesi, isaretli olanin yolu, kapisinin isareti ve adi
///     ciziliyor. Isaret kaldirilan cikis haritada hic gorunmuyor (16.11.2026'ya kadar isareti ve adi kaliyordu). Ustteki
///     arama kutusu listeyi ada gore suzuyor; "All on / All off" yalniz gorunen satirlara uygulanir. Degisiklik aninda
///     uygulaniyor, pencere kapaninca diske yaziliyor. Liste pencere acilirken dolduruluyor; oyunda yeni bir cikis
///     gorulurse bir sonraki acilista gelir.
/// </summary>
internal sealed class PathPanel : Panel
{
    private readonly OverlaySettings settings;
    private readonly CheckedListBox list;
    private bool filling;

    internal PathPanel(OverlaySettings settings, Action apply)
    {
        this.settings = settings;

        var label = new Label
        {
            Location = new Point(12, 12),
            Size = new Size(456, 36),
            Text = "Exit paths: untick an exit to hide it from the map completely (path, marker and name). " +
                   "New exits are shown by default.",
        };

        var searchLabel = new Label { Location = new Point(12, 55), Size = new Size(52, 20), Text = "Search:" };
        var search = new TextBox { Location = new Point(66, 52), Size = new Size(402, 23) };

        this.list = new CheckedListBox
        {
            Location = new Point(12, 82),
            Size = new Size(456, 300),
            CheckOnClick = true,
            IntegralHeight = false,
        };

        this.list.ItemCheck += (_, e) =>
        {
            if (this.filling) { return; }

            var name = (string)this.list.Items[e.Index];
            if (e.NewValue == CheckState.Checked) { settings.HiddenPaths.Remove(name); }
            else { settings.HiddenPaths.Add(name); }

            apply();
        };

        search.TextChanged += (_, _) => this.Fill(search.Text);
        this.Fill("");

        var allOn = new Button { Location = new Point(12, 390), Size = new Size(108, 28), Text = "All on" };
        allOn.Click += (_, _) => this.SetAll(true);

        var allOff = new Button { Location = new Point(128, 390), Size = new Size(108, 28), Text = "All off" };
        allOff.Click += (_, _) => this.SetAll(false);

        var hint = new Label
        {
            Location = new Point(12, 428),
            Size = new Size(456, 80),
            ForeColor = SystemColors.GrayText,
            Text = (settings.SeenPaths.Count == 0 ? "No exits seen yet: they are listed here once the overlay has found them in game.\n\n" : "") +
                   "A name counts everywhere it appears (for example \"Exit\" in every area). " +
                   "All on / All off change only the exits shown in the list (use Search to narrow it). " +
                   "The exit paths key (General tab) still hides or shows all paths at once.",
        };

        this.Controls.AddRange(new Control[] { label, searchLabel, search, this.list, allOn, allOff, hint });
    }

    /// <summary>Listeyi adinda <paramref name="filter"/> gecen cikislarla yeniden doldurur (buyuk/kucuk harf farksiz).</summary>
    private void Fill(string filter)
    {
        filter = filter.Trim();
        this.filling = true;
        this.list.BeginUpdate();
        try
        {
            this.list.Items.Clear();
            foreach (var name in this.settings.SeenPaths)
            {
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) { continue; }

                this.list.Items.Add(name, !this.settings.HiddenPaths.Contains(name));
            }
        }
        finally
        {
            this.list.EndUpdate();
            this.filling = false;
        }
    }

    private void SetAll(bool on)
    {
        for (var i = 0; i < this.list.Items.Count; i++) { this.list.SetItemChecked(i, on); }
    }
}
