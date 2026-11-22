namespace Poe2Map;

/// <summary>
///     "guncelleme = acik / kapali": acilista GitHub'da yeni surum var mi diye bakilsin mi (OverlayContext.Update.cs).
///     Varsayilan acik; kapaliyken GitHub'a hic sorulmuyor.
/// </summary>
internal sealed partial class OverlaySettings
{
    private const string CheckUpdatesKey = "guncelleme";

    internal bool CheckUpdates { get; set; } = true;

    private void WriteUpdateLines(List<string> lines)
    {
        lines.Add("");
        lines.Add("# Acilista GitHub'da yeni surum var mi diye bak (kurmadan once sorar)");
        lines.Add($"{CheckUpdatesKey} = {(this.CheckUpdates ? "acik" : "kapali")}");
    }
}
