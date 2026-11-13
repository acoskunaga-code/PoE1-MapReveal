namespace Poe2Map;

/// <summary>
///     Kisayol secme kutusu (General sekmesi): kutuya tiklayip bir tusa (istenirse Ctrl / Alt / Shift ile) basinca
///     o tus seciliyor. Delete ya da Backspace kisayolu kaldiriyor. Tek basina Ctrl / Alt / Shift secilmiyor.
///     Secim reddedilirse (diger kisayolun tusu) kutu eski tusu gostermeye devam ediyor.
/// </summary>
internal sealed class HotKeyBox : TextBox
{
    private readonly Func<Keys, bool> changed;
    private Keys current;

    internal HotKeyBox(Keys current, Func<Keys, bool> changed)
    {
        this.changed = changed;
        this.current = current;
        this.ReadOnly = true;
        this.BackColor = SystemColors.Window;
        this.Cursor = Cursors.Hand;
        this.ShortcutsEnabled = false;
        this.Text = GlobalHotKey.Describe(current);
    }

    // Tab, ok tuslari gibi tuslar da secilebilsin (normalde pencerede gezinmeye gidiyorlar).
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        var key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) { return; }

        var chosen = key is Keys.Delete or Keys.Back ? Keys.None : e.KeyData;
        if (!this.changed(chosen)) { return; }

        this.current = chosen;
        this.Text = GlobalHotKey.Describe(this.current);
    }
}
