namespace Poe2Map;

/// <summary>
///     Ayar penceresinin "Altars" sekmesi: Eldritch altar seceneklerinden hangilerinin cercevelenecegi ve hangi
///     renkle. "Player gains" kutusu bir renk (varsayilan yesil); asagidaki kutuya her satira bir yazi, o yazi gecen
///     secenek (kim kazanirsa kazansin) ikinci renk (varsayilan altin). Degisiklik aninda uygulaniyor, pencere
///     kapaninca diske yaziliyor.
/// </summary>
internal sealed class AltarPanel : Panel
{
    private readonly OverlaySettings settings;
    private readonly Action apply;

    internal AltarPanel(OverlaySettings settings, Action apply)
    {
        this.settings = settings;
        this.apply = apply;

        var player = new CheckBox
        {
            AutoSize = true,
            Location = new Point(12, 14),
            Text = "Frame \"Player gains\" choices",
            Checked = settings.AltarPlayer,
        };
        player.CheckedChanged += (_, _) =>
        {
            settings.AltarPlayer = player.Checked;
            apply();
        };

        var playerColor = this.ColorButton(new Point(360, 10), () => settings.AltarPlayerColor, c => settings.AltarPlayerColor = c);

        var label = new Label
        {
            AutoSize = true,
            Location = new Point(12, 52),
            Text = "Also frame choices that contain any of these, one per line:",
        };

        var textColor = this.ColorButton(new Point(360, 46), () => settings.AltarTextColor, c => settings.AltarTextColor = c);

        var phrases = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Location = new Point(12, 80),
            Size = new Size(456, 210),
            Lines = settings.AltarPhrases.ToArray(),
        };
        phrases.TextChanged += (_, _) =>
        {
            settings.AltarPhrases = phrases.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            apply();
        };

        // The Viridian Wildwood (OverlayForm.Wildwood.cs): wisp'ler renkli noktalar, kureler ve Funerary altarlari adiyla.
        var wildwood = new CheckBox
        {
            AutoSize = true,
            Location = new Point(12, 300),
            Text = "Wildwood: wisps (blue / yellow / purple), orbs, Funerary altars",
            Checked = settings.Wildwood,
        };
        wildwood.CheckedChanged += (_, _) =>
        {
            settings.Wildwood = wildwood.Checked;
            apply();
        };

        // Djinn's Cache sandiklari (OverlayForm.Caches.cs): harita simgeleri olmadigi icin Icons sekmesinde degiller.
        var caches = new CheckBox
        {
            AutoSize = true,
            Location = new Point(12, 326),
            Text = "Djinn's Caches: chest marker with metal and contents",
            Checked = settings.DjinnCaches,
        };
        caches.CheckedChanged += (_, _) =>
        {
            settings.DjinnCaches = caches.Checked;
            apply();
        };

        var hint = new Label
        {
            Location = new Point(12, 358),
            Size = new Size(456, 150),
            ForeColor = SystemColors.GrayText,
            Text = "A choice is framed when its text contains the line, whoever gains it (Player, Map boss, " +
                   "Eldritch Minions). If a choice is both, the two frames are drawn one inside the other.\n\n" +
                   "Numbers and ranges do not matter: \"(1.6-3.2)% chance to drop an additional Divine Orb\" " +
                   "copied from poedb matches \"2.4% chance to drop an additional Divine Orb\" in the game. " +
                   "Upper / lower case does not matter. A short part like \"Divine Orb\" is enough.",
        };

        this.Controls.AddRange(new Control[] { player, playerColor, label, textColor, phrases, wildwood, caches, hint });
    }

    /// <summary>Rengi gosteren ve tiklaninca renk penceresini acan dugme (ColorPick).</summary>
    private Button ColorButton(Point at, Func<Color> get, Action<Color> set) => ColorPick.Button(this, at, get, set, this.apply);
}
