namespace Poe2Map;

/// <summary>
///     Programin yazdigi butun dosyalarin klasoru: gunluk, ayarlar, ogrenilen tarifler (alan tutucu, altar yolu),
///     indirilen simgeler. 13.11.2026 (kullanici: dosyalari kendi klasorunde olustursun): exe'nin yanindaki data\.
///     Orasi yazilamiyorsa (exe Program Files gibi korumali bir yerde) eskisi gibi %LOCALAPPDATA%\poe1-map.
///
///     Eski surumlerin %LOCALAPPDATA%\poe1-map klasoru overlay'in ilk acilisinda buraya tasiniyor (<see cref="MoveOldFiles"/>).
/// </summary>
internal static class AppPaths
{
    internal static readonly string OldDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "poe1-map");

    private static string? dir;

    internal static string Dir => dir ??= Pick();

    internal static string File(string name) => Path.Combine(Dir, name);

    private static string Pick()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            Directory.CreateDirectory(local);
            var probe = Path.Combine(local, $".write-test-{Environment.ProcessId}");
            System.IO.File.WriteAllText(probe, "");
            System.IO.File.Delete(probe);
            return local;
        }
        catch
        {
            return OldDir;
        }
    }

    /// <summary>
    ///     Eski klasordeki dosyalar (ayarlar, tarifler, simgeler) yeni klasorde yoksa oraya tasinir; yeni klasorde zaten
    ///     varsa eski kopya ve eski gunlukler silinir. Eski klasor bosalirsa o da silinir. Sonuc gunluk icin bir satir;
    ///     tasinacak bir sey yoksa null. Hata olursa eski dosyalar yerinde kalir, program yeni klasorle devam eder.
    /// </summary>
    internal static string? MoveOldFiles()
    {
        if (string.Equals(Path.GetFullPath(Dir).TrimEnd('\\'), Path.GetFullPath(OldDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(OldDir))
        {
            return null;
        }

        int moved = 0, dropped = 0, failed = 0;
        foreach (var source in Directory.GetFiles(OldDir, "*", SearchOption.AllDirectories))
        {
            try
            {
                var target = Path.Combine(Dir, Path.GetRelativePath(OldDir, source));
                if (source.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || System.IO.File.Exists(target))
                {
                    System.IO.File.Delete(source);
                    dropped++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                System.IO.File.Move(source, target);
                moved++;
            }
            catch
            {
                failed++;
            }
        }

        try
        {
            foreach (var sub in Directory.GetDirectories(OldDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(sub).Any()) { Directory.Delete(sub); }
            }

            if (!Directory.EnumerateFileSystemEntries(OldDir).Any()) { Directory.Delete(OldDir); }
        }
        catch
        {
            // Bos klasor kalirsa zarari yok.
        }

        return $"eski klasor {OldDir} -> {Dir}: {moved} dosya tasindi, {dropped} silindi (gunluk ya da yenisi vardi), {failed} tasinamadi";
    }
}
