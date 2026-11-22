using System.Diagnostics;
using System.Text;

namespace Poe2Map;

/// <summary>
///     Bir overlay karesinin bolum bolum suresi (21.11.2026, overlay'in islemci yukunu ekrana bakmadan izlemek icin):
///     okuma, pencere, arka plan, cizim katmanlari, ekrana basma. Her bolumun kare basina ortalama ms'si ve en uzunu,
///     30 saniyede bir tek satir olarak gunluge. Yalnizca overlay gorunurken atilan kareler.
/// </summary>
internal sealed class PaintProfile
{
    private const long EveryMs = 30_000;

    private readonly Dictionary<string, (double Sum, double Max)> parts = new();
    private readonly List<string> order = [];
    private long mark;
    private long lastFlush = Environment.TickCount64;
    private int frames;
    private bool open;

    internal void Begin()
    {
        this.mark = Stopwatch.GetTimestamp();
        this.frames++;
        this.open = true;
    }

    /// <summary>Karenin sonu: sonraki Begin'e kadar gelen isaretler (kare disi bir cizim) sayilmaz.</summary>
    internal void End() => this.open = false;

    internal void Mark(string name)
    {
        if (!this.open) { return; }

        var now = Stopwatch.GetTimestamp();
        var ms = (now - this.mark) * 1000.0 / Stopwatch.Frequency;
        this.mark = now;
        if (!this.parts.TryGetValue(name, out var v)) { this.order.Add(name); }
        this.parts[name] = (v.Sum + ms, Math.Max(v.Max, ms));
    }

    /// <summary>30 saniyede bir ozet (harita cizilen kare yoksa yazilmaz), sonra sifirlanir.</summary>
    internal string? Summary(int cacheBuilds)
    {
        var now = Environment.TickCount64;
        if (now - this.lastFlush < EveryMs) { return null; }

        var seconds = (now - this.lastFlush) / 1000.0;
        this.lastFlush = now;
        this.open = false;
        if (this.frames == 0) { return null; }

        var text = new StringBuilder($"kare bolumleri ({this.frames} gorunur kare / {seconds:F0} sn, kare basina ort/en uzun ms):");
        var total = 0.0;
        foreach (var name in this.order)
        {
            var (sum, max) = this.parts[name];
            total += sum;
            text.Append($"  {name} {sum / this.frames:F2}/{max:F1}");
        }

        text.Append($"  toplam {total / this.frames:F2}  zemin onbellegi {cacheBuilds} kez cizildi");
        this.parts.Clear();
        this.order.Clear();
        this.frames = 0;
        return text.ToString();
    }
}
