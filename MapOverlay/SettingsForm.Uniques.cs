namespace Poe2Map;

/// <summary>
///     "Uniques" sekmesi: ustte arama (Icons sekmesindeki gibi, 250 ms bekleyerek), haritadaki unique adlarinin yazi
///     tipi ve rengi (butun unique'ler icin tek ayar), altta UniquesPanel. Liste dosyasi (data\unique-tiers.txt) ucuncu
///     taraf verisi oldugu icin surumde yok: "Build list" dugmesi onu kullanicinin bilgisayarinda uretiyor (UniqueTierBuilder:
///     poewiki + istege bagli kendi loot filtresi; RePoE uniques.json yoksa o da iniyor).
/// </summary>
internal sealed partial class SettingsForm
{
    private TabPage BuildUniquesTab()
    {
        var page = new TabPage("Uniques") { UseVisualStyleBackColor = true };
        this.FillUniquesTab(page);
        return page;
    }

    /// <summary>Sekmenin icini (yeniden) kurar: liste varsa tablo, yoksa aciklama ve "Build list" dugmesi.</summary>
    private void FillUniquesTab(TabPage page)
    {
        foreach (var old in page.Controls.Cast<Control>().ToList()) { old.Dispose(); }
        page.Controls.Clear();

        if (UniqueList.Load() is not { } list)
        {
            var note = new Label
            {
                Location = new Point(14, 14),
                Size = new Size(560, 120),
                Text = "The unique list is not built yet.\n\n" +
                       "\"Build list\" downloads the unique names and base types from poewiki.net (and, if missing, the unique " +
                       "pictures list from RePoE) and groups them the way your loot filter does (choose your .filter file) or by " +
                       "item type. It takes a few seconds and needs internet.\n\nAll uniques start switched off.",
            };
            var build = new Button { Location = new Point(14, 140), Size = new Size(140, 30), Text = "Build list..." };
            build.Click += (_, _) => this.BuildUniqueList(page, build);
            page.Controls.Add(note);
            page.Controls.Add(build);
            return;
        }

        var panel = new UniquesPanel(this.settings, list, this.apply) { Dock = DockStyle.Fill };
        var top = new Panel { Dock = DockStyle.Top, Height = 92 };

        top.Controls.Add(new Label { AutoSize = true, Location = new Point(8, 11), Text = "Search" });
        var search = new TextBox { Location = new Point(60, 8), Width = 400, PlaceholderText = "unique or base, e.g. mageblood, heavy belt" };
        var rebuild = new Button { Location = new Point(468, 6), Size = new Size(112, 26), Text = "Rebuild list..." };
        rebuild.Click += (_, _) => this.BuildUniqueList(page, rebuild);
        top.Controls.Add(rebuild);
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

        // Haritadaki adlar: yazi tipi ve renk, butun unique'ler icin.
        var fontButton = new Button { Location = new Point(8, 38), Size = new Size(230, 26), TextAlign = ContentAlignment.MiddleLeft };
        fontButton.Text = this.FontText();
        fontButton.Click += (_, _) =>
        {
            using var current = this.settings.CreateUniqueFont();
            using var dialog = new FontDialog { Font = current, ShowEffects = false, MinSize = 6, MaxSize = 40 };
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }

            this.settings.UniqueFontName = dialog.Font.FontFamily.Name;
            this.settings.UniqueFontSize = dialog.Font.SizeInPoints;
            this.settings.UniqueFontBold = dialog.Font.Bold;
            fontButton.Text = this.FontText();
            this.apply();
        };
        top.Controls.Add(fontButton);

        var colorButton = new Button { Location = new Point(246, 38), Size = new Size(110, 26), Text = "Name colour", FlatStyle = FlatStyle.Flat };
        colorButton.BackColor = this.settings.UniqueColor;
        colorButton.ForeColor = this.settings.UniqueColor.GetBrightness() > 0.5f ? Color.Black : Color.White;
        colorButton.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = this.settings.UniqueColor, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }

            this.settings.UniqueColor = dialog.Color;
            colorButton.BackColor = dialog.Color;
            colorButton.ForeColor = dialog.Color.GetBrightness() > 0.5f ? Color.Black : Color.White;
            this.apply();
        };
        top.Controls.Add(colorButton);

        // Alert: isaretli unique yerdeyken ekranin ustunde buyuk harflerle adi; boyutu hepsi icin tek.
        top.Controls.Add(new Label { AutoSize = true, Location = new Point(368, 43), Text = "Alert size" });
        var alertSize = new NumericUpDown { Location = new Point(432, 40), Width = 56, Minimum = 12, Maximum = 120 };
        alertSize.Value = Math.Clamp(this.settings.UniqueAlertSize, 12, 120);
        alertSize.ValueChanged += (_, _) =>
        {
            this.settings.UniqueAlertSize = (int)alertSize.Value;
            this.apply();
        };
        top.Controls.Add(alertSize);

        top.Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(8, 68),
            Size = new Size(580, 20),
            ForeColor = SystemColors.GrayText,
            Text = "On the ground: Show = name on map, Path = path, Alert = big name on screen, Frame = frame its label.",
        });

        // Dolduran panel once eklenmeli: kenarlara yaslanan (ust) oge ondan sonra yerlesiyor.
        page.Controls.Add(panel);
        page.Controls.Add(top);
    }

    /// <summary>
    ///     Listeyi kullanicinin bilgisayarinda uretir: once filtre sorusu (Evet = .filter sec, Hayir = esya sinifina gore),
    ///     sonra arka planda indirme ve yazma; bitince sekme yeniden kurulur. Ayarlar (isaretler) ada gore, korunuyor.
    /// </summary>
    private void BuildUniqueList(TabPage page, Button button)
    {
        var answer = MessageBox.Show(
            this,
            "Group the uniques the way your loot filter does?\n\nYes: choose your .filter file\nNo: group them by item type",
            "Build unique list",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) { return; }

        string? filter = null;
        if (answer == DialogResult.Yes)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Choose your loot filter",
                Filter = "Loot filter (*.filter)|*.filter|All files (*.*)|*.*",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Path of Exile"),
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }

            filter = dialog.FileName;
        }

        button.Enabled = false;
        button.Text = "Building...";
        Task.Run(() =>
        {
            try
            {
                var report = UniqueTierBuilder.Build(filter, UniqueList.FilePath);
                if (!File.Exists(UniqueArt.FileName))
                {
                    UniqueTierBuilder.DownloadRePoE(UniqueArt.FileName);
                    UniqueArt.Reload();
                }

                CrashLog.Write($"unique listesi uretildi ({(filter is null ? "esya sinifina gore" : Path.GetFileName(filter))}): {string.Join("; ", report)}", null);
                return (string?)null;
            }
            catch (Exception ex)
            {
                CrashLog.Write("unique listesi uretilemedi", ex);
                return ex.Message;
            }
        }).ContinueWith(done =>
        {
            if (this.IsDisposed) { return; }

            this.BeginInvoke(() =>
            {
                if (done.Result is { } error)
                {
                    button.Enabled = true;
                    button.Text = "Build list...";
                    MessageBox.Show(this, $"The list could not be built:\n{error}", "Build unique list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                this.FillUniquesTab(page);
            });
        });
    }

    /// <summary>Adi verilen sekmeyi secer (buyuk/kucuk harf farksiz); yoksa bir sey yapmaz.</summary>
    internal void SelectTab(string name)
    {
        if (this.Controls.OfType<TabControl>().FirstOrDefault() is not { } tabs) { return; }

        foreach (TabPage page in tabs.TabPages)
        {
            if (page.Text.Equals(name, StringComparison.OrdinalIgnoreCase)) { tabs.SelectedTab = page; }
        }
    }

    private string FontText() =>
        $"Name font: {this.settings.UniqueFontName}, {this.settings.UniqueFontSize:0.#} pt{(this.settings.UniqueFontBold ? ", bold" : "")}";
}
