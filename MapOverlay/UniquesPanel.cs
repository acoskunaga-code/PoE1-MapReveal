namespace Poe2Map;

/// <summary>
///     Ayar penceresinin "Uniques" sekmesi: unique'ler filtredeki gruplarina gore (UniqueList, filtredeki sirayla: T1,
///     T2, ...). Grup basligina tiklayinca acilir / kapanir. Her satirda "Show" (yerdeyse haritada adi yazilir) ve "Path"
///     (yerdeyse haritada ona yol cizilir) kutusu; baslik satirindaki kutular butun grubu degistirir. Arama (SettingsForm)
///     butun gruplardan eslesenleri acar. Kullanici istegi 18.11.2026.
///
///     Tek DataGridView: 1200'u askin unique icin satir basina ayri denetim (Icons sekmesi gibi) pencereyi yavaslatirdi.
/// </summary>
internal sealed class UniquesPanel : Panel
{
    private const int ShowColumn = 2;
    private const int PathColumn = 3;
    private const int AlertColumn = 4;
    private const int FrameColumn = 5;

    private readonly OverlaySettings settings;
    private readonly Action apply;
    private readonly UniqueList list;
    private readonly DataGridView grid;
    private readonly HashSet<string> open = new(StringComparer.Ordinal);
    private readonly Dictionary<UniqueList.Group, DataGridViewRow> headers = new();
    private readonly Font bold;
    private string query = "";
    private bool filling;

    internal UniquesPanel(OverlaySettings settings, UniqueList list, Action apply)
    {
        this.settings = settings;
        this.list = list;
        this.apply = apply;

        this.grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };
        this.bold = new Font(this.grid.Font, FontStyle.Bold);

        this.grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Unique", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        this.grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Base", ReadOnly = true, Width = 150, SortMode = DataGridViewColumnSortMode.NotSortable, DefaultCellStyle = { ForeColor = SystemColors.GrayText } });
        this.grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Show", Width = 46 });
        this.grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Path", Width = 46 });
        this.grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Alert", Width = 46, ToolTipText = "Big name on the screen while it is on the ground" });
        this.grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Frame", Width = 48, ToolTipText = "Frame the game's own label of the item on the ground" });

        this.Fill();

        // Kutu degistigi anda isle. Eskiden CellContentClick'te isleniyordu: hizli ikinci tiklama cift tiklama sayiliyor,
        // kutu degisiyor ama CellContentClick gelmiyordu; deger satirdan cikana kadar islenmiyordu (kullanici 18.11.2026:
        // "hizlica acip kapatinca guncellenmiyor").
        this.grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (this.grid.IsCurrentCellDirty && this.grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                this.grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        this.grid.CellValueChanged += (_, e) => this.ValueChanged(e.RowIndex, e.ColumnIndex);
        this.grid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex < ShowColumn && this.grid.Rows[e.RowIndex].Tag is UniqueList.Group group) { this.ToggleGroup(group); }
        };

        // Acilista ilk satir secili (mavi) gelmesin.
        this.grid.VisibleChanged += (_, _) =>
        {
            this.grid.ClearSelection();
            this.grid.CurrentCell = null;
        };

        this.Controls.Add(this.grid);
    }

    /// <summary>Arama: bos degilse butun gruplardan adinda ya da tabaninda gecenler, gruplari acik.</summary>
    internal void Search(string text)
    {
        this.query = text.Trim();
        this.UpdateVisibility();
    }

    private void Fill()
    {
        this.filling = true;
        var rows = new List<DataGridViewRow>();
        foreach (var group in this.list.Groups)
        {
            var header = new DataGridViewRow { Tag = group, DefaultCellStyle = { Font = this.bold, BackColor = SystemColors.ControlLight } };
            header.CreateCells(this.grid, "", "", false, false, false, false);
            rows.Add(header);
            this.headers[group] = header;

            foreach (var entry in group.Entries)
            {
                var row = new DataGridViewRow { Tag = (group, entry), Visible = false };
                row.CreateCells(
                    this.grid, entry.Name, entry.Base, this.settings.ShownUniques.Contains(entry.Name),
                    this.settings.PathUniques.Contains(entry.Name), this.settings.AlertUniques.Contains(entry.Name),
                    this.settings.FrameUniques.Contains(entry.Name));
                rows.Add(row);
            }
        }

        this.grid.Rows.AddRange(rows.ToArray());
        foreach (var group in this.list.Groups) { this.UpdateHeader(group); }
        this.filling = false;
    }

    private void ToggleGroup(UniqueList.Group group)
    {
        if (this.query.Length > 0) { return; }
        if (!this.open.Remove(group.Id)) { this.open.Add(group.Id); }

        this.UpdateVisibility();
    }

    private bool Matches(UniqueList.Entry entry) =>
        entry.Name.Contains(this.query, StringComparison.OrdinalIgnoreCase) ||
        entry.Base.Contains(this.query, StringComparison.OrdinalIgnoreCase);

    private void UpdateVisibility()
    {
        // Gecerli satir gizlenemiyor (DataGridView istisna atiyor): once secimi birak.
        this.grid.CurrentCell = null;
        this.grid.SuspendLayout();
        var searching = this.query.Length > 0;
        foreach (DataGridViewRow row in this.grid.Rows)
        {
            switch (row.Tag)
            {
                case UniqueList.Group group:
                    row.Visible = !searching || group.Entries.Any(this.Matches);
                    this.UpdateHeader(group);
                    break;
                case (UniqueList.Group group, UniqueList.Entry entry):
                    row.Visible = searching ? this.Matches(entry) : this.open.Contains(group.Id);
                    break;
            }
        }

        this.grid.ResumeLayout();
    }

    private void ValueChanged(int rowIndex, int column)
    {
        if (this.filling || rowIndex < 0 || column < ShowColumn) { return; }

        var row = this.grid.Rows[rowIndex];
        var on = row.Cells[column].Value is true;
        this.filling = true;
        try
        {
            switch (row.Tag)
            {
                case (UniqueList.Group group, UniqueList.Entry entry):
                    this.Set(entry.Name, column, on);
                    this.UpdateHeader(group);
                    break;
                case UniqueList.Group group:
                    // Baslik kutusu: butun grup (arama varken yalniz gorunenler).
                    foreach (DataGridViewRow r in this.grid.Rows)
                    {
                        if (r.Tag is not (UniqueList.Group g, UniqueList.Entry e) || g != group) { continue; }
                        if (this.query.Length > 0 && !this.Matches(e)) { continue; }

                        r.Cells[column].Value = on;
                        this.Set(e.Name, column, on);
                    }

                    this.UpdateHeader(group);
                    break;
            }
        }
        finally
        {
            this.filling = false;
        }

        this.apply();
    }

    private void Set(string name, int column, bool on)
    {
        var set = column switch
        {
            ShowColumn => this.settings.ShownUniques,
            PathColumn => this.settings.PathUniques,
            AlertColumn => this.settings.AlertUniques,
            _ => this.settings.FrameUniques,
        };

        if (on) { set.Add(name); }
        else { set.Remove(name); }
    }

    /// <summary>
    ///     Baslik: ok, ad, sayi; kutular gruptaki satirlarin hepsi isaretliyse isaretli. Arama varken yalniz gorunen
    ///     (eslesen) satirlara bakiyor, cunku baslik kutusu da yalniz onlari degistiriyor (18.11.2026: "leather belt"
    ///     aramasinda 12 Leather Belt isaretliyken baslik bos gorunuyordu, gruptaki Heavy Belt'ler yuzunden).
    /// </summary>
    private void UpdateHeader(UniqueList.Group group)
    {
        var header = this.headers[group];
        var searching = this.query.Length > 0;
        var rows = searching ? group.Entries.Where(this.Matches).ToList() : group.Entries;
        var wasFilling = this.filling;
        this.filling = true;
        header.Cells[0].Value = $"{(searching || this.open.Contains(group.Id) ? "▾" : "▸")}  {group.Title}  ({rows.Count})";
        header.Cells[ShowColumn].Value = rows.Count > 0 && rows.All(e => this.settings.ShownUniques.Contains(e.Name));
        header.Cells[PathColumn].Value = rows.Count > 0 && rows.All(e => this.settings.PathUniques.Contains(e.Name));
        header.Cells[AlertColumn].Value = rows.Count > 0 && rows.All(e => this.settings.AlertUniques.Contains(e.Name));
        header.Cells[FrameColumn].Value = rows.Count > 0 && rows.All(e => this.settings.FrameUniques.Contains(e.Name));
        this.filling = wasFilling;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { this.bold.Dispose(); }

        base.Dispose(disposing);
    }
}
