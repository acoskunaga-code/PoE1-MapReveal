namespace Poe2Map;

/// <summary>
///     Acilista bir kez guncelleme sorusu (UpdateFeed, UpdateWindow, UpdateInstall, UpdateApply). General sekmesindeki
///     kutu kapaliysa GitHub'a hic sorulmuyor. "Not now": bir sonraki acilista yeniden sorulur.
/// </summary>
internal sealed partial class OverlayContext
{
    private UpdateWindow? updateWindow;

    private void StartUpdateCheck()
    {
        var finished = UpdateApply.Finish(Environment.GetCommandLineArgs());
        if (finished is { } done)
        {
            this.tray.ShowBalloonTip(
                5000,
                "PoE1 Map",
                done ? $"Updated to {UpdateFeed.Text(UpdateFeed.Current)}." : "The update failed. The previous version is running.",
                done ? ToolTipIcon.Info : ToolTipIcon.Warning);

            // Basarisiz denemeden hemen sonra ayni soru tekrar acilmasin.
            if (!done) { return; }
        }

        if (!this.settings.CheckUpdates) { return; }

        Task.Run(this.CheckUpdateAsync);
    }

    private async Task CheckUpdateAsync()
    {
        try
        {
            var current = UpdateFeed.Text(UpdateFeed.Current);
            if (await UpdateFeed.CheckAsync().ConfigureAwait(false) is not { } release)
            {
                CrashLog.Write($"guncelleme: yeni surum yok (bu surum {current})", null);
                return;
            }

            var next = UpdateFeed.Text(release.Version);
            if (!UpdateInstall.CanInstall)
            {
                CrashLog.Write($"guncelleme: {next} var; gelistirme derlemesi ({current}), sorulmuyor", null);
                return;
            }

            CrashLog.Write($"guncelleme: {next} var (bu surum {current}), soruluyor", null);
            this.Post(() => this.AskUpdate(release));
        }
        catch (Exception ex)
        {
            // Internet yok, GitHub cevap vermiyor, depo kapali: harita bundan etkilenmez.
            CrashLog.Write($"guncelleme denetlenemedi: {ex.Message}", null);
        }
    }

    private void AskUpdate(UpdateFeed.Release release)
    {
        if (this.exiting) { return; }

        this.updateWindow = new UpdateWindow(release, this.ExitThread);
        this.updateWindow.Show();
    }
}
