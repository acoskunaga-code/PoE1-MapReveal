using System.Diagnostics;

namespace Poe2Map;

/// <summary>
///     Varlik turunun (RefreshMonsters, 250 ms'de bir) suresi gunlukte, 30 sn'de bir "varlik turu" satiri: tur sayisi,
///     ortalama ve en uzun sure, ust uste bindigi icin atlanan tur. Yeni dusen esyanin adi / cercevesi / uyarisi bu turla
///     geliyor; "gec ciziyor" sikayetinde once buraya bakilir (kullanici 21.11.2026: "esyalari gec ciziyor
///     belirginlestiriyor").
/// </summary>
internal sealed partial class OverlayForm
{
    private long turnTicks;
    private long turnMaxTicks;
    private int turns;
    private int turnsSkipped;
    private long turnLogAt = Stopwatch.GetTimestamp();

    private void NoteTurnSkipped() => Interlocked.Increment(ref this.turnsSkipped);

    /// <summary>RefreshMonsters'in sonunda (tek is parcacigi: turlar ust uste binmiyor).</summary>
    private void NoteTurn(long started)
    {
        var now = Stopwatch.GetTimestamp();
        var ticks = now - started;
        this.turnTicks += ticks;
        this.turnMaxTicks = Math.Max(this.turnMaxTicks, ticks);
        this.turns++;

        if (Stopwatch.GetElapsedTime(this.turnLogAt, now).TotalSeconds < 30) { return; }

        var skipped = Interlocked.Exchange(ref this.turnsSkipped, 0);
        var ms = 1000.0 / Stopwatch.Frequency;
        CrashLog.Write($"varlik turu: {this.turns} tur / 30 sn, ort {this.turnTicks * ms / this.turns:F1} ms, en uzun {this.turnMaxTicks * ms:F1} ms, atlanan {skipped}", null);
        this.turnTicks = 0;
        this.turnMaxTicks = 0;
        this.turns = 0;
        this.turnLogAt = now;
    }

    // Yolu istenen unique ilk kez gorulunce yollar 5 saniyelik turu beklemeden hesaplansin (OverlayForm.Paths.cs).
    private HashSet<ulong> uniquePathAsked = new();

    /// <summary>RefreshMonsters'tan, trackedUniques yazildiktan sonra.</summary>
    private void AskUniquePaths()
    {
        var fresh = this.trackedUniques.Where(u => this.UniqueHasPath(u.Name) && !this.uniquePathAsked.Contains(u.Render)).ToList();
        if (fresh.Count == 0 || Volatile.Read(ref this.pathRefreshing) == 1) { return; }

        foreach (var u in fresh) { this.uniquePathAsked.Add(u.Render); }
        this.pathTimer?.Change(0, Volatile.Read(ref this.exitRuns) >= ExitWarmupRuns ? ExitRefreshMs : ExitWarmupMs);
    }
}
