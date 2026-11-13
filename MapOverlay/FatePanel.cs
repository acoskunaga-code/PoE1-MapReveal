namespace Poe2Map;

/// <summary>
///     Ayar penceresinin "Fates" sekmesi: Fate panelinde kartlarin ustune odul yazilari yazilsin mi, ve hangi yazilar
///     gecen odul renkli yazilip kartin kutusu cercevelensin (altarlardaki gibi, her satira bir yazi). Degisiklik aninda
///     uygulaniyor, pencere kapaninca diske yaziliyor.
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
            Location = new Point(12, 52),
            Text = "Frame cards whose rewards contain any of these, one per line:",
        };

        var color = ColorPick.Button(this, new Point(360, 46), () => settings.FateTextColor, c => settings.FateTextColor = c, apply);

        var phrases = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Location = new Point(12, 80),
            Size = new Size(456, 210),
            Lines = settings.FatePhrases.ToArray(),
        };
        phrases.TextChanged += (_, _) =>
        {
            settings.FatePhrases = phrases.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            apply();
        };

        var hint = new Label
        {
            Location = new Point(12, 300),
            Size = new Size(456, 200),
            ForeColor = SystemColors.GrayText,
            Text = "When the \"Select a Fate\" or \"Invoke Fates\" panel opens, each card's rewards are written above it. " +
                   "A reward that contains one of the lines is written in the colour above and the card's box gets a " +
                   "frame in that colour.\n\n" +
                   "Examples: Unique Jewellery, Item Rarity, Divine, Rare Rings. Upper / lower case and numbers do not " +
                   "matter; a short part of the text is enough.",
        };

        this.Controls.AddRange(new Control[] { show, label, color, phrases, hint });
    }
}
