using System.Diagnostics;

namespace Poe2Map;

/// <summary>
///     "Guncelleme var, ister misin?" penceresi, surum notlariyla (UpdateNotes). Hicbir sey "Update now" tiklanmadan
///     indirilmez. Odak "Not now"da: oyunda basilan Enter ya da bosluk yanlislikla guncelleme baslatmasin. Ekrandaki yazi
///     Ingilizce.
/// </summary>
internal sealed class UpdateWindow : Form
{
    private readonly UpdateFeed.Release release;
    private readonly Action restart;
    private readonly CancellationTokenSource cancel = new();
    private readonly Label status = new();
    private readonly ProgressBar bar = new() { Visible = false };
    private readonly Button update = new() { Text = "Update now" };
    private readonly Button later = new() { Text = "Not now" };

    internal UpdateWindow(UpdateFeed.Release release, Action restart)
    {
        this.release = release;
        this.restart = restart;

        this.Text = "PoE1 Map - Update";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.TopMost = true;
        this.ClientSize = new Size(560, 476);

        var title = new Label
        {
            Text = $"Version {UpdateFeed.Text(release.Version)} is available (you have {UpdateFeed.Text(UpdateFeed.Current)}).",
            Font = new Font(this.Font, FontStyle.Bold),
        };
        title.SetBounds(16, 14, 528, 22);

        var whatsNew = new Label { Text = "What's new:", AutoSize = true, Location = new Point(16, 42) };
        var changes = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Text = release.Notes,
        };
        changes.SetBounds(16, 64, 528, 268);

        this.status.Text = "Do you want to update? It is downloaded from GitHub, then the app restarts. Your settings are kept.";
        this.status.SetBounds(16, 342, 528, 48);
        this.bar.SetBounds(16, 396, 528, 16);

        var notes = new LinkLabel { Text = "Full release notes", AutoSize = true, Location = new Point(16, 436), Visible = release.Page.Length > 0 };
        notes.LinkClicked += (_, _) => this.OpenNotes();

        this.update.SetBounds(332, 428, 112, 30);
        this.update.Click += async (_, _) => await this.InstallAsync();
        this.later.SetBounds(452, 428, 92, 30);
        this.later.Click += (_, _) => this.Close();
        this.CancelButton = this.later;

        // Ekleme sirasi = sekme sirasi: ilk odak "Not now".
        this.Controls.AddRange(new Control[] { this.later, this.update, notes, title, whatsNew, changes, this.status, this.bar });
        this.ActiveControl = this.later;
        this.FormClosed += (_, _) => this.cancel.Cancel();
    }

    private void OpenNotes()
    {
        try { using var process = Process.Start(new ProcessStartInfo(this.release.Page) { UseShellExecute = true }); }
        catch (Exception ex) { CrashLog.Write("surum notlari acilamadi", ex); }
    }

    private async Task InstallAsync()
    {
        if (Environment.ProcessPath is not { } target) { return; }

        this.update.Enabled = false;
        this.later.Text = "Cancel";
        this.bar.Value = 0;
        this.bar.Visible = true;
        this.status.Text = "Downloading...";
        var progress = new Progress<int>(percent =>
        {
            if (!this.IsDisposed) { this.bar.Value = Math.Clamp(percent, 0, 100); }
        });

        try
        {
            CrashLog.Write($"guncelleme onaylandi: {UpdateFeed.Text(UpdateFeed.Current)} -> {UpdateFeed.Text(this.release.Version)}", null);
            var helper = await Task.Run(() => UpdateInstall.PrepareAsync(this.release, target, progress, this.cancel.Token));
            if (this.IsDisposed || this.cancel.IsCancellationRequested)
            {
                UpdateInstall.TryDelete(helper);
                return;
            }

            UpdateInstall.StartHelper(helper, target);
            this.restart();
        }
        catch (OperationCanceledException) when (this.cancel.IsCancellationRequested)
        {
            CrashLog.Write("guncelleme iptal edildi", null);
        }
        catch (Exception ex)
        {
            CrashLog.Write("guncelleme indirilemedi", ex);
            UpdateInstall.TryDelete(UpdateInstall.HelperPath(target));
            if (this.IsDisposed) { return; }

            var reason = ex switch
            {
                UnauthorizedAccessException => "The app's folder is write-protected.",
                InvalidDataException => ex.Message,
                _ => "The download or the start of the new version failed.",
            };
            this.status.Text = $"Update failed: {reason}\nYour current version still works.";
            this.bar.Visible = false;
            this.update.Enabled = true;
            this.update.Text = "Try again";
            this.later.Text = "Close";
        }
    }
}
