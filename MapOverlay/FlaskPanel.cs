namespace Poe2Map;

/// <summary>
///     Flasks sekmesi (AutoKeys.cs): can ya da energy shield esigin altina inince basilacak tus.
///
///         [x] Life below           [50] %   key [1]   wait [1000] ms
///         [x] Energy shield below  [50] %   key [2]   wait [1000] ms
///         Now: Life 1/1  Mana 280/727  Energy shield 6686/6788 (98%)
///
///     Ayarlar aninda gecerli (AutoKeys her turda ayar nesnesini okuyor); "Now" satiri yarim saniyede bir yenileniyor
///     ki okunan degerlerin oyundakiyle ayni oldugu gorulsun.
/// </summary>
internal sealed class FlaskPanel : Panel
{
    internal FlaskPanel(OverlaySettings settings)
    {
        AddRow(this, "Life below", 16, settings.AutoLife);
        AddRow(this, "Energy shield below", 52, settings.AutoEs);

        var now = new Label { AutoSize = false, Font = new Font(this.Font, FontStyle.Bold) };
        now.SetBounds(16, 96, 560, 22);
        this.Controls.Add(now);

        var hint = new Label { AutoSize = false, ForeColor = SystemColors.GrayText };
        hint.SetBounds(16, 128, 560, 90);
        hint.Text = "The key is pressed only while the game window is in front and you are alive, at most once per " +
                    "\"wait\" for each row. Click a key box, then press the key (Delete = no key).\n\n" +
                    "A character without energy shield never triggers the second row; with Chaos Inoculation " +
                    "(life 1/1) the first row never triggers.";
        this.Controls.Add(hint);

        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => now.Text = Describe(AutoKeys.Last);
        timer.Start();
        now.Text = Describe(AutoKeys.Last);
        this.Disposed += (_, _) => timer.Dispose();
    }

    private static string Describe(PlayerVitals.Vitals? vitals) => vitals is { } v
        ? $"Now: Life {v.Life} ({v.Life.Percent}%)   Mana {v.Mana}   Energy shield {v.Es}" +
          (v.Es.Percent is { } es ? $" ({es}%)" : "")
        : "Now: not in game (or the character is not found yet)";

    private static void AddRow(Panel panel, string text, int top, OverlaySettings.AutoKeyRule rule)
    {
        var enabled = new CheckBox { Text = text, Checked = rule.Enabled, AutoSize = false };
        enabled.SetBounds(16, top, 150, 24);
        enabled.CheckedChanged += (_, _) => rule.Enabled = enabled.Checked;

        var percent = new NumericUpDown { Minimum = 1, Maximum = 99, Value = Math.Clamp(rule.Percent, 1, 99) };
        percent.SetBounds(170, top + 1, 52, 24);
        percent.ValueChanged += (_, _) => rule.Percent = (int)percent.Value;

        var percentLabel = new Label { Text = "%   key", AutoSize = true };
        percentLabel.SetBounds(226, top + 4, 50, 20);

        // Iki satira ayni tus verilebilir (ayni flask); kisayol tuslariyla cakisma kontrolu yok, onlar ayri.
        var key = new HotKeyBox(rule.Key, k =>
        {
            rule.Key = k;
            return true;
        });
        key.SetBounds(280, top, 90, 24);

        var waitLabel = new Label { Text = "wait", AutoSize = true };
        waitLabel.SetBounds(380, top + 4, 34, 20);

        var wait = new NumericUpDown { Minimum = 100, Maximum = 60_000, Increment = 100, Value = Math.Clamp(rule.WaitMs, 100, 60_000) };
        wait.SetBounds(416, top + 1, 70, 24);
        wait.ValueChanged += (_, _) => rule.WaitMs = (int)wait.Value;

        var msLabel = new Label { Text = "ms", AutoSize = true };
        msLabel.SetBounds(490, top + 4, 30, 20);

        panel.Controls.AddRange(new Control[] { enabled, percent, percentLabel, key, waitLabel, wait, msLabel });
    }
}
