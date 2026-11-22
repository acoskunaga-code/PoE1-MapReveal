using System.Runtime.InteropServices;

namespace Poe2Map;

/// <summary>
///     Can ya da energy shield esigin altina inince ayarlanan tusa basar (Flasks sekmesi, OverlaySettings.AutoKeys.cs).
///     Kendi is parcaciginda 50 ms'de bir: oyun penceresi on planda degilse, oyuncu bulunamiyorsa ya da olu ise
///     (can 0, yukleme ekrani) hicbir sey yapmaz. Her kural icin iki basis arasinda en az "WaitMs" bekler.
///     Kendi surec tutamacini aciyor: denetleyicinin tutamaci oyun yeniden baslayinca kapatiliyor.
/// </summary>
internal sealed class AutoKeys : IDisposable
{
    private const int Interval = 50;

    private readonly OverlaySettings settings;
    private readonly Func<int> gamePid;
    private readonly Thread thread;
    private volatile bool stop;

    private IntPtr handle;
    private int pid;
    private ulong entity;
    private ulong lifeComponent;
    private long nextLife;
    private long nextEs;

    internal AutoKeys(OverlaySettings settings, Func<int> gamePid)
    {
        this.settings = settings;
        this.gamePid = gamePid;
        this.thread = new Thread(this.Run) { IsBackground = true, Name = "AutoKeys" };
        this.thread.Start();
    }

    /// <summary>Son okunan havuzlar (Flasks sekmesi canli gosteriyor); okunamadiysa null.</summary>
    internal static PlayerVitals.Vitals? Last { get; private set; }

    public void Dispose()
    {
        this.stop = true;
        this.thread.Join(500);
        if (this.handle != IntPtr.Zero) { Native.CloseHandle(this.handle); }
    }

    private void Run()
    {
        while (!this.stop)
        {
            try
            {
                this.Step();
            }
            catch (Exception ex)
            {
                CrashLog.Write("oto tus hatasi", ex);
                Thread.Sleep(1000);
            }

            Thread.Sleep(Interval);
        }
    }

    private void Step()
    {
        Last = this.ReadVitals();
        if (Last is not { } v || v.Life.Current <= 0) { return; }
        if (!this.settings.AutoLife.Enabled && !this.settings.AutoEs.Enabled) { return; }
        if (!GameInFront()) { return; }

        var now = Environment.TickCount64;
        this.Check(this.settings.AutoLife, v.Life, "can", now, ref this.nextLife);
        this.Check(this.settings.AutoEs, v.Es, "es", now, ref this.nextEs);
    }

    private void Check(OverlaySettings.AutoKeyRule rule, PlayerVitals.Pool pool, string what, long now, ref long next)
    {
        if (!rule.Enabled || rule.Key == Keys.None || pool.Percent is not { } percent) { return; }
        if (percent >= rule.Percent || now < next) { return; }

        next = now + rule.WaitMs;
        KeyPress.Send(rule.Key);
        CrashLog.Write($"oto tus: {what} {pool} (%{percent} < %{rule.Percent}) -> {GlobalHotKey.Describe(rule.Key)}", null);
    }

    private PlayerVitals.Vitals? ReadVitals()
    {
        if (!this.EnsureHandle() || LocalPlayer.Current is not { } lp) { return null; }

        if (lp.Entity != this.entity || this.lifeComponent == 0)
        {
            this.entity = lp.Entity;
            this.lifeComponent = EntityReader.ReadInfo(this.handle, lp.Entity) is { } info &&
                                 info.Components.TryGetValue("Life", out var life) ? life : 0;
            if (this.lifeComponent != 0 && PlayerVitals.Read(this.handle, this.lifeComponent) is { } first)
            {
                CrashLog.Write($"oto tus: oyuncu havuzlari can {first.Life}  mana {first.Mana}  es {first.Es}", null);
            }
        }

        return this.lifeComponent == 0 ? null : PlayerVitals.Read(this.handle, this.lifeComponent);
    }

    private bool EnsureHandle()
    {
        var current = this.gamePid();
        if (current == this.pid && this.handle != IntPtr.Zero) { return true; }

        if (this.handle != IntPtr.Zero) { Native.CloseHandle(this.handle); }
        this.handle = IntPtr.Zero;
        this.pid = current;
        this.entity = 0;
        if (current == 0) { return false; }

        this.handle = Native.OpenProcess(Native.ProcessQueryInformation | Native.ProcessVmRead, false, current);
        return this.handle != IntPtr.Zero;
    }

    private bool GameInFront()
    {
        var window = GetForegroundWindow();
        return window != IntPtr.Zero && GetWindowThreadProcessId(window, out var owner) != 0 && owner == this.pid;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
}
