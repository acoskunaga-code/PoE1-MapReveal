namespace Poe2Map;

/// <summary>Oyunda gorulen cikis adlari ayarlarin listesine eklenir (Paths sekmesi); yeni ad varsayilan olarak acik.</summary>
internal sealed partial class OverlayContext
{
    private void NoteExitNames(IReadOnlyList<string> names)
    {
        var added = names.Where(n => n.Length > 0 && this.settings.SeenPaths.Add(n)).ToList();
        if (added.Count > 0) { CrashLog.Write("yeni cikis adlari (Paths sekmesi): " + string.Join(", ", added), null); }
    }
}
