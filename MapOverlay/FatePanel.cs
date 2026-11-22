namespace Poe2Map;

/// <summary>
///     Ayar penceresinin "Fates" sekmesi: odulunde hangi yazilar gecen kart cercevelensin (altarlardaki gibi, her satira
///     bir yazi), en iyi secim onerisi ve odul degerleri (FateValues). Degisiklik aninda uygulaniyor, pencere kapaninca
///     diske yaziliyor.
/// </summary>
internal sealed class FatePanel : Panel
{
    internal FatePanel(OverlaySettings settings, Action apply)
    {
        var label = new Label
        {
            AutoSize = true,
            Location = new Point(12, 18),
            Text = "Frame cards whose rewards contain any of these, one per line:",
        };

        var color = ColorPick.Button(this, new Point(360, 12), () => settings.FateTextColor, c => settings.FateTextColor = c, apply);

        var phrases = Lines(new Point(12, 42), 100, settings.FatePhrases, l => settings.FatePhrases = l, apply);

        // Invoke Fates: en iyi kartlar ve tiklama sirasi (OverlayForm.FatePlan.cs), degerler asagidaki listeden.
        var plan = new CheckBox
        {
            AutoSize = true,
            Location = new Point(12, 156),
            Text = "Suggest cards: TAKE on Select a Fate, click order on Invoke Fates",
            Checked = settings.FatePlan,
        };
        plan.CheckedChanged += (_, _) =>
        {
            settings.FatePlan = plan.Checked;
            apply();
        };

        var valueLabel = new Label
        {
            AutoSize = true,
            Location = new Point(12, 184),
            Text = "Reward value in chaos for the suggestion, one per line:",
        };

        var valueBox = Lines(new Point(12, 208), 150, settings.FateValueLines, l => settings.FateValueLines = l, apply);
        var reset = new Button { Text = "Defaults", Location = new Point(388, 178), Size = new Size(80, 26) };
        reset.Click += (_, _) => valueBox.Lines = FateValues.Defaults.ToArray();

        var hint = new Label
        {
            Location = new Point(12, 366),
            Size = new Size(456, 120),
            ForeColor = SystemColors.GrayText,
            Text = "A reward is worth the first line it contains; [2] or [3] limits a line to rewards that need that " +
                   "many copies. Rewards matching no line are worth 1.\n\n" +
                   "Two cards with the same name: the first one clicked counts three times, the second is lost. Three: " +
                   "the first and third count three times. A \"two / three copies\" reward adds up over all invoked " +
                   "cards, so different cards can complete it together.",
        };

        this.Controls.AddRange(new Control[] { label, color, phrases, plan, valueLabel, valueBox, reset, hint });
    }

    /// <summary>Her satira bir yazi alan cok satirli kutu; degisince bos satirlar atilip ayara yaziliyor.</summary>
    private static TextBox Lines(Point at, int height, IEnumerable<string> lines, Action<List<string>> set, Action apply)
    {
        var box = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Location = at,
            Size = new Size(456, height),
            Lines = lines.ToArray(),
        };
        box.TextChanged += (_, _) =>
        {
            set(box.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList());
            apply();
        };
        return box;
    }
}
