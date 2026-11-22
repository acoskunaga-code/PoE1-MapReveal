using System.Diagnostics;
using System.Globalization;

namespace Poe2Map;

/// <summary>
///     "&lt;ad&gt;.update.exe --apply-update pid hedef": yeni surum eskisinin kapanmasini bekler, onun yerine gecer ve onu
///     KENDI ADIYLA baslatir (kullanici exe'yi yeniden adlandirmis olabilir, ad korunur). Yeni exe calisamiyorsa bu adim
///     hic olmaz, eski surum yerinde kalir. Her adim gunlukte.
///
///     Hedef yalnizca bu dosyanin adindan cikan exe olabilir; arguman baska bir dosyanin ustune yazdiramaz.
///     Bu arguman bicimi ESKI surumlerden cagriliyor: degistirme.
/// </summary>
internal static class UpdateApply
{
    internal const string Argument = "--apply-update";
    internal const string UpdatedArgument = "--updated";
    internal const string FailedArgument = "--update-failed";

    private const int WaitMs = 30_000;
    private const int Tries = 20;

    /// <summary>true: bu calisma bir guncelleme adimiydi, uygulama acilmayacak.</summary>
    internal static bool Run(string[] args)
    {
        if (args.Length == 0 || args[0] != Argument) { return false; }

        if (args.Length != 3 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
            Environment.ProcessPath is not { } self)
        {
            CrashLog.Write("guncelleme: gecersiz arguman", null);
            return true;
        }

        var target = Path.GetFullPath(args[2]);
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target) ||
            !string.Equals(UpdateInstall.HelperPath(target), self, StringComparison.OrdinalIgnoreCase))
        {
            CrashLog.Write("guncelleme: hedef bu dosyaya ait degil, dokunulmadi", null);
            return true;
        }

        if (!WaitForExit(pid))
        {
            CrashLog.Write("guncelleme: eski surum kapanmadi, dokunulmadi", null);
            return true;
        }

        var done = Replace(self, target);
        try
        {
            var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) };
            start.ArgumentList.Add(done ? UpdatedArgument : FailedArgument);
            using var process = Process.Start(start);
        }
        catch (Exception ex)
        {
            CrashLog.Write("guncelleme: uygulama yeniden baslatilamadi", ex);
        }

        return true;
    }

    private static bool WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(WaitMs);
        }
        catch (ArgumentException)
        {
            return true; // coktan kapanmis
        }
        catch (Exception ex)
        {
            CrashLog.Write("guncelleme: eski surum beklenemedi", ex);
            return false;
        }
    }

    /// <summary>Kopya once yanina, sonra iki ad degisimi: hicbir anda yarim yazilmis exe hedef adinda durmuyor.</summary>
    private static bool Replace(string self, string target)
    {
        var fresh = target + ".new";
        var old = target + ".old";
        try
        {
            Retry(() => File.Copy(self, fresh, overwrite: true));
            Retry(() => File.Move(target, old, overwrite: true));
            try
            {
                Retry(() => File.Move(fresh, target));
            }
            catch
            {
                Retry(() => File.Move(old, target));
                throw;
            }

            CrashLog.Write($"guncelleme: {UpdateFeed.Text(UpdateFeed.Current)} yerine gecti ({Path.GetFileName(target)})", null);
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("guncelleme yapilamadi, eski surum yerinde", ex);
            UpdateInstall.TryDelete(fresh);
            return false;
        }
    }

    /// <summary>Virus tarayicisi ve yeni kapanan surec dosyayi bir sure tutabiliyor.</summary>
    private static void Retry(Action action)
    {
        for (var i = 1; ; i++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (i < Tries && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>
    ///     Guncellemenin baslattigi acilis: yardimci ve eski exe silinir. Yalniz bu acilista: kullanicinin benzer adli
    ///     baska bir dosyasina dokunulmasin. null = guncelleme acilisi degil.
    /// </summary>
    internal static bool? Finish(string[] args)
    {
        var done = args.Contains(UpdatedArgument);
        if ((!done && !args.Contains(FailedArgument)) || Environment.ProcessPath is not { } exe) { return null; }

        Task.Run(() =>
        {
            foreach (var file in new[] { UpdateInstall.HelperPath(exe), exe + ".old" })
            {
                for (var i = 0; i < Tries && File.Exists(file); i++)
                {
                    UpdateInstall.TryDelete(file);
                    if (File.Exists(file)) { Thread.Sleep(500); }
                }
            }
        });
        return done;
    }
}
