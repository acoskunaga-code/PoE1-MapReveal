namespace Poe2Map;

/// <summary>Ayar sekmelerinin renk dugmesi: rengi gosteriyor, tiklaninca renk penceresini aciyor (Altars, Fates).</summary>
internal static class ColorPick
{
    internal static Button Button(Control owner, Point at, Func<Color> get, Action<Color> set, Action apply)
    {
        var button = new Button
        {
            Location = at,
            Size = new Size(108, 28),
            Text = "Colour",
            FlatStyle = FlatStyle.Flat,
            BackColor = get(),
        };
        button.ForeColor = Readable(button.BackColor);
        button.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = get(), FullOpen = true };
            if (dialog.ShowDialog(owner.FindForm()) != DialogResult.OK) { return; }

            set(dialog.Color);
            button.BackColor = dialog.Color;
            button.ForeColor = Readable(dialog.Color);
            apply();
        };

        return button;
    }

    /// <summary>Dugme yazisi renkli zeminde okunsun: acik zeminde siyah, koyu zeminde beyaz.</summary>
    private static Color Readable(Color back) =>
        (back.R * 299) + (back.G * 587) + (back.B * 114) > 140_000 ? Color.Black : Color.White;
}
