namespace Poe2Map;

/// <summary>
///     Ayar penceresi, iki sekme: "General" (harita cizgisi ve canavar simgeleri, <see cref="MonsterPanel"/>) ve
///     "Icons" (harita simgeleri, <see cref="IconsPanel"/>). Degisiklik aninda haritaya uygulaniyor,
///     "Tamam"a basmak gerekmiyor; pencere kapaninca diske yaziliyor.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly OverlaySettings settings;
    private readonly Action apply;
    private readonly Button colorButton = new();
    private readonly NumericUpDown thicknessBox = new();
    // Denetleyicinin (OverlayContext) katalogu: haritadaki simgeler de onu kullaniyor, pencere kapaninca kapatilmiyor.
    private readonly IconCatalog? catalog;

    internal SettingsForm(OverlaySettings settings, Action apply, IconCatalog? catalog)
    {
        this.settings = settings;
        this.apply = apply;
        this.catalog = catalog;

        this.Text = "PoE1 Map - Settings";
        this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.TopMost = true;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(this.BuildGeneralTab());
        tabs.TabPages.Add(this.BuildIconsTab());

        // Eldritch altar secenekleri (AltarPanel.cs): "Player gains" yesil, kullanicinin yazdigi metinler altin.
        var altars = new TabPage("Altars") { UseVisualStyleBackColor = true };
        altars.Controls.Add(new AltarPanel(this.settings, this.apply) { Dock = DockStyle.Fill });
        tabs.TabPages.Add(altars);

        // Fate paneli (FatePanel.cs): kartlarin ustunde odul yazilari, kullanicinin yazdigi metinler renkli ve cerceveli.
        var fates = new TabPage("Fates") { UseVisualStyleBackColor = true };
        fates.Controls.Add(new FatePanel(this.settings, this.apply) { Dock = DockStyle.Fill });
        tabs.TabPages.Add(fates);

        // Cikis yollari (PathPanel.cs): gorulen cikislardan hangilerinin yolu cizilsin.
        var paths = new TabPage("Paths") { UseVisualStyleBackColor = true };
        paths.Controls.Add(new PathPanel(this.settings, this.apply) { Dock = DockStyle.Fill });
        tabs.TabPages.Add(paths);
        this.Controls.Add(tabs);

        this.ClientSize = new Size(500, 560);
        this.FormClosed += (_, _) => this.settings.Save();
    }

    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("General") { UseVisualStyleBackColor = true };

        var colorLabel = new Label { Text = "Line colour", AutoSize = true };
        colorLabel.SetBounds(16, 20, 100, 20);

        this.colorButton.SetBounds(130, 14, 110, 28);
        this.colorButton.FlatStyle = FlatStyle.Flat;
        this.colorButton.BackColor = this.settings.LineColor;
        this.colorButton.Click += (_, _) => this.PickColor();

        var thicknessLabel = new Label { Text = "Line thickness", AutoSize = true };
        thicknessLabel.SetBounds(16, 60, 100, 20);

        this.thicknessBox.SetBounds(130, 56, 110, 24);
        this.thicknessBox.Minimum = 1;
        this.thicknessBox.Maximum = OverlayForm.MaxThickness;
        this.thicknessBox.Value = Math.Clamp(this.settings.Thickness, 1, OverlayForm.MaxThickness);
        this.thicknessBox.ValueChanged += (_, _) =>
        {
            this.settings.Thickness = (int)this.thicknessBox.Value;
            this.apply();
        };

        var monsters = new MonsterPanel(this.settings, this.apply) { Location = new Point(12, 92) };

        // Kisayollar (GlobalHotKey): oyun on plandayken de calisiyor. Iki kutuya ayni tus verilemiyor.
        var keyTop = monsters.Bottom + 18;
        var keyHint = new Label { ForeColor = SystemColors.GrayText, AutoSize = false };
        keyHint.SetBounds(16, keyTop + 64, 460, 36);
        const string HintText = "Click a box, then press a key (Ctrl / Alt / Shift allowed). Delete = no key. " +
                                "Exit paths are on at every start.";
        keyHint.Text = HintText;

        bool Taken(Keys key, Keys other)
        {
            var taken = key != Keys.None && key == other;
            keyHint.Text = taken ? $"{GlobalHotKey.Describe(key)} is already used by the other key." : HintText;
            return taken;
        }

        AddKeyRow(page, "On / off key", keyTop, this.settings.ToggleKey, key =>
        {
            if (Taken(key, this.settings.PathKey)) { return false; }

            this.settings.ToggleKey = key;
            this.apply();
            return true;
        });
        AddKeyRow(page, "Exit paths key", keyTop + 32, this.settings.PathKey, key =>
        {
            if (Taken(key, this.settings.ToggleKey)) { return false; }

            this.settings.PathKey = key;
            this.apply();
            return true;
        });

        page.Controls.AddRange(new Control[]
        {
            colorLabel, this.colorButton, thicknessLabel, this.thicknessBox, monsters, keyHint,
        });
        return page;
    }

    /// <summary>Ad + tus kutusu satiri. <paramref name="chosen"/> false donerse kutu eski tusa geri doner.</summary>
    private static void AddKeyRow(TabPage page, string text, int top, Keys current, Func<Keys, bool> chosen)
    {
        var label = new Label { Text = text, AutoSize = true };
        label.SetBounds(16, top + 4, 110, 20);
        var box = new HotKeyBox(current, chosen);
        box.SetBounds(130, top, 110, 24);
        page.Controls.Add(label);
        page.Controls.Add(box);
    }

    /// <summary>
    ///     Simge katalogu (data/icons/catalog.txt + Icons.png) yoksa sekme neyin eksik oldugunu soyluyor; var ise
    ///     kategorili liste. Veri ucuncu taraf oldugu icin depoda yok, bkz. data/README.md.
    /// </summary>
    private TabPage BuildIconsTab()
    {
        var page = new TabPage("Icons") { UseVisualStyleBackColor = true };

        if (this.catalog is null)
        {
            page.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                Text = "Icon data is not here yet.\n\nIt is downloaded automatically when the overlay starts " +
                       "(about 9 MB, needs internet). Close and reopen this window in a moment; if it still shows " +
                       "this, restart the overlay.",
            });
            return page;
        }

        var panel = new IconsPanel(this.settings, this.catalog, this.apply) { Dock = DockStyle.Fill };
        var top = new Panel { Dock = DockStyle.Top, Height = 84 };

        // Arama en ustte (IconsPanel.Search.cs). Her tusta yuzlerce satir kurulmasin: yazmayi 250 ms bekliyor.
        top.Controls.Add(new Label { AutoSize = true, Location = new Point(8, 11), Text = "Search" });
        var search = new TextBox { Location = new Point(60, 8), Width = 410, PlaceholderText = "e.g. delir (all categories)" };
        var wait = new System.Windows.Forms.Timer { Interval = 250 };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            panel.Search(search.Text);
        };
        search.TextChanged += (_, _) =>
        {
            wait.Stop();
            wait.Start();
        };
        page.Disposed += (_, _) => wait.Dispose();
        top.Controls.Add(search);

        top.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(8, 38),
            Text = this.catalog.HasImages
                ? "Icon size in pixels, 0 = off"
                : "Icon size in pixels, 0 = off (Icons.png not found: no previews, nothing drawn on the map)",
        });

        // Oyundaki ad: shrine turu ("Lightning Shrine"), strongbox turu ("Armourer's Strongbox"), NPC adi.
        var names = new CheckBox
        {
            AutoSize = true,
            Location = new Point(10, 59),
            Text = "Show names next to icons (shrine type, strongbox type, NPC)",
            Checked = this.settings.IconNames,
        };
        names.CheckedChanged += (_, _) =>
        {
            this.settings.IconNames = names.Checked;
            this.apply();
        };
        top.Controls.Add(names);

        // Dolduran panel once eklenmeli: kenarlara yaslanan (ust) oge ondan sonra yerlesiyor.
        page.Controls.Add(panel);
        page.Controls.Add(top);
        return page;
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { Color = this.settings.LineColor, FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) { return; }

        this.settings.LineColor = dialog.Color;
        this.colorButton.BackColor = dialog.Color;
        this.apply();
    }
}
