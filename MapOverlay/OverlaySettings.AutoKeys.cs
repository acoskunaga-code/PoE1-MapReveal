namespace Poe2Map;

/// <summary>
///     Can ya da energy shield esigin altina inince basilan tus (Flasks sekmesi, AutoKeys.cs). Kullanici 20.11.2026:
///     "can ve energy shield azalmasi durumunda basilacak bir tus ayarlayabilirmisin". Varsayilan kapali.
///
///         oto-can = kapali 50 1000 D1        acik/kapali, yuzde esigi, iki basis arasi en az ms, tus (Keys adi)
///         oto-es  = kapali 50 1000 D2
///
///     Tus en sonda: "D1, Control" gibi virgullu adlar da okunuyor.
/// </summary>
internal sealed partial class OverlaySettings
{
    private const string AutoLifeKey = "oto-can";
    private const string AutoEsKey = "oto-es";

    internal sealed class AutoKeyRule
    {
        internal bool Enabled { get; set; }

        internal int Percent { get; set; } = 50;

        internal int WaitMs { get; set; } = 1000;

        internal Keys Key { get; set; }
    }

    internal AutoKeyRule AutoLife { get; } = new() { Key = Keys.D1 };

    internal AutoKeyRule AutoEs { get; } = new() { Key = Keys.D2 };

    private static void ReadAutoKeyLine(OverlaySettings settings, string key, string value)
    {
        var rule = key == AutoLifeKey ? settings.AutoLife : settings.AutoEs;
        var parts = value.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 0) { rule.Enabled = parts[0].Equals("acik", StringComparison.OrdinalIgnoreCase); }
        if (parts.Length > 1 && int.TryParse(parts[1], out var percent)) { rule.Percent = Math.Clamp(percent, 1, 99); }
        if (parts.Length > 2 && int.TryParse(parts[2], out var wait)) { rule.WaitMs = Math.Clamp(wait, 100, 60_000); }
        if (parts.Length > 3 && ReadKey(parts[3]) is { } k) { rule.Key = k; }
    }

    private void WriteAutoKeyLines(List<string> lines)
    {
        static string Line(AutoKeyRule r) => $"{(r.Enabled ? "acik" : "kapali")} {r.Percent} {r.WaitMs} {r.Key}";

        lines.Add("");
        lines.Add("# Can / ES esigin altina inince basilan tus (Flasks sekmesi): acik/kapali, yuzde, en az ms, tus");
        lines.Add($"{AutoLifeKey} = {Line(this.AutoLife)}");
        lines.Add($"{AutoEsKey}  = {Line(this.AutoEs)}");
    }
}
