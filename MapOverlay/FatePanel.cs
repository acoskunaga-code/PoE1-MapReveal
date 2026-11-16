namespace Poe2Map;

/// <summary>
///     Ayar penceresinin "Fates" sekmesi: Fate panelinde kartlarin ustune odul yazilari yazilsin mi, hangi yazilar gecen
///     odul renkli yazilip kartin kutusu cercevelensin (altarlardaki gibi, her satira bir yazi), ve Invoke Fates panelinde
///     en iyi secim onerisi ile onun odul sirasi (FatePlanner). Degisiklik aninda uygulaniyor, pencere kapaninca diske
///     yaziliyor.
/// </summary>
internal sealed class FatePanel : Panel
{
    internal FatePanel(OverlaySettings settings, Action apply)
    {
        var show = new CheckBox
        {
            AutoSize = true,
            Location = new Point(12, 14),
            Text = "Show Fate rewards above the cards",
            Checked = settings.FateRewards,
        };
        show.CheckedChanged += (_, _) =>
        {
            settings.FateRewards = show.Checked;
            apply();
        };

        var label = new Label
        {
            AutoSize = true,
            Location = new Point(12, 46),
            Text = "Frame cards whose rewards contain any of these, one per line:",
        };

        var color = ColorPick.Button(this, new Point(360, 40), () => settings.FateTextColor, c => settings.FateTextColor = c, apply);

        var phrases = Lines(new Point(12, 70), 100, settings.FatePhrases, l => settings.FatePhrases = l, apply);

        // Invoke Fates: en iyi kartlar ve tiklama sirasi (OverlayForm.FatePlan.cs), sira asagidaki listeden.
        var plan = new CheckBox
        {
            AutoSize = true,
            Location = new Point(12, 184),
            Text = "Suggest cards: TAKE on Select a Fate, click order on Invoke Fates",
            Checked = settings.FatePlan,
        };
        plan.CheckedChanged += (_, _) =>
        {
            settings.FatePlan = plan.Checked;
            apply();
        };

        var rankLabel = new Label
        {
            AutoSize = true,
            Location = new Point(12, 212),
            Text = "Reward order for the suggestion, best first, one per line:",
        };

        var ranking = Lines(new Point(12, 236), 150, settings.FateRanking, l => settings.FateRanking = l, apply);

        var hint = new Label
        {
            Location = new Point(12, 394),
            Size = new Size(456, 120),
            ForeColor = SystemColors.GrayText,
            Text = "A reward is ranked by the first line it contains (\"Valuable Currency\" above \"Currency\"); rewards " +
                   "matching no line count least. Two cards with the same name: the one clicked first keeps its rewards " +
                   "three times and opens its \"two copies\" reward, the other one's rewards are lost. Such a combination " +
                   "beats a single reward that is a few lines higher (Unique x3 beats one Currency).\n\n" +
                   "Upper / lower case and numbers do not matter; a short part of the text is enough.",
        };

        this.Controls.AddRange(new Control[] { show, label, color, phrases, plan, rankLabel, ranking, hint });
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
