namespace Poe2Map;

/// <summary>
///     Ayar penceresinin "Paths" sekmesi: gorulen cikislarin listesi, isaretli olanin yolu ciziliyor. Isaret kaldirilan
///     cikisin yolu cizilmiyor; kapinin isareti ve adi haritada kaliyor. Degisiklik aninda uygulaniyor, pencere kapaninca
///     diske yaziliyor. Liste pencere acilirken dolduruluyor; oyunda yeni bir cikis gorulurse bir sonraki acilista gelir.
/// </summary>
internal sealed class PathPanel : Panel
{
    internal PathPanel(OverlaySettings settings, Action apply)
    {
        var label = new Label
        {
            Location = new Point(12, 12),
            Size = new Size(456, 36),
            Text = "Exit paths: untick an exit to stop drawing its path. Its marker and name stay on the map. " +
                   "New exits are drawn by default.",
        };

        var list = new CheckedListBox
        {
            Location = new Point(12, 52),
            Size = new Size(456, 330),
            CheckOnClick = true,
            IntegralHeight = false,
        };

        foreach (var name in settings.SeenPaths)
        {
            list.Items.Add(name, !settings.HiddenPaths.Contains(name));
        }

        list.ItemCheck += (_, e) =>
        {
            var name = (string)list.Items[e.Index];
            if (e.NewValue == CheckState.Checked) { settings.HiddenPaths.Remove(name); }
            else { settings.HiddenPaths.Add(name); }

            apply();
        };

        var allOn = new Button { Location = new Point(12, 390), Size = new Size(108, 28), Text = "All on" };
        allOn.Click += (_, _) => SetAll(list, true);

        var allOff = new Button { Location = new Point(128, 390), Size = new Size(108, 28), Text = "All off" };
        allOff.Click += (_, _) => SetAll(list, false);

        var hint = new Label
        {
            Location = new Point(12, 428),
            Size = new Size(456, 80),
            ForeColor = SystemColors.GrayText,
            Text = (settings.SeenPaths.Count == 0 ? "No exits seen yet: they are listed here once the overlay has found them in game.\n\n" : "") +
                   "A name counts everywhere it appears (for example \"Exit\" in every area). " +
                   "The exit paths key (General tab) still hides or shows all paths at once.",
        };

        this.Controls.AddRange(new Control[] { label, list, allOn, allOff, hint });
    }

    private static void SetAll(CheckedListBox list, bool on)
    {
        for (var i = 0; i < list.Items.Count; i++) { list.SetItemChecked(i, on); }
    }
}
