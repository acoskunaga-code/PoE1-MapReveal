using System.Runtime.InteropServices;

namespace Poe2Map;

/// <summary>
///     Sistem genelinde bir kisayol tusu (RegisterHotKey): oyun on plandayken de calisiyor, oyuna hicbir sey
///     gonderilmiyor; Windows tusu oyundan once bize veriyor (tus o sirada oyuna gitmiyor). Mesajlari almak icin
///     gorunmez bir mesaj penceresi. Tus basili tutulunca tekrar etmesin diye MOD_NOREPEAT.
/// </summary>
internal sealed class GlobalHotKey : NativeWindow, IDisposable
{
    private const int WmHotKey = 0x0312;

    // Kimlikler: 0x5031 overlay acma/kapama, 0x5032 cikis yollari. Test icin WM_HOTKEY bu kimlikle gonderiliyor.
    internal const int ToggleId = 0x5031;
    internal const int PathsId = 0x5032;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModNoRepeat = 0x4000;

    private readonly int id;
    private bool registered;

    internal GlobalHotKey(int id)
    {
        this.id = id;

        // HWND_MESSAGE (-3): sadece mesaj alan, ekranda hic gorunmeyen pencere.
        this.CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
    }

    internal event Action? Pressed;

    /// <summary>Su an kayitli tus (Keys.None = yok).</summary>
    internal Keys Current { get; private set; } = Keys.None;

    /// <summary>Tusu kaydeder (oncekini birakir). Baska bir program ayni tusu tutuyorsa false.</summary>
    internal bool Register(Keys keys)
    {
        if (keys == this.Current && this.registered) { return true; }

        this.Unregister();
        this.Current = keys;
        if (keys == Keys.None) { return true; }

        uint mods = ModNoRepeat;
        if ((keys & Keys.Alt) != 0) { mods |= ModAlt; }
        if ((keys & Keys.Control) != 0) { mods |= ModControl; }
        if ((keys & Keys.Shift) != 0) { mods |= ModShift; }

        this.registered = RegisterHotKey(this.Handle, this.id, mods, (uint)(keys & Keys.KeyCode));
        return this.registered;
    }

    public void Dispose()
    {
        this.Unregister();
        this.DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && m.WParam.ToInt32() == this.id) { this.Pressed?.Invoke(); }

        base.WndProc(ref m);
    }

    private void Unregister()
    {
        if (!this.registered) { return; }

        UnregisterHotKey(this.Handle, this.id);
        this.registered = false;
    }

    /// <summary>Tusun okunur adi: "F9", "Ctrl+F9".</summary>
    internal static string Describe(Keys keys)
    {
        if (keys == Keys.None) { return "none"; }

        var parts = new List<string>();
        if ((keys & Keys.Control) != 0) { parts.Add("Ctrl"); }
        if ((keys & Keys.Alt) != 0) { parts.Add("Alt"); }
        if ((keys & Keys.Shift) != 0) { parts.Add("Shift"); }
        parts.Add((keys & Keys.KeyCode).ToString());
        return string.Join("+", parts);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
