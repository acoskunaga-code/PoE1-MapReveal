using System.Numerics;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     "Invoke Fates" panelinde eldeki kartlardan en iyi secimi ve tiklama sirasini bulur.
///
///     Oyunun kurallari (15.11.2026, canli, iki Golemancer + Sanguine, sira degistirilerek iki kez okundu):
///         - kartlar ustteki yuvalara tiklama sirasiyla giriyor, yuvayi oyuncu secemiyor
///         - "kopya" = AYNI ADLI kart (odulleri farkli olabilir)
///         - ayni addan ilk tiklanan ANA kart; digerinin kendi odulleri (kopya odulleri dahil) tamamen kayboluyor
///         - 2 kopyada ana kartin normal odulleri 3 kat, "invoke two copies" odulleri 1 kat aciliyor
///         - tek kart normal odullerini 1 kat veriyor, kopya odulu acilmiyor
///     3 kopya henuz gorulmedi: carpan 2 kopyadaki gibi sayiliyor, "three copies" odulu aciliyor (DOGRULANMADI).
///
///     Puan: kullanicinin sirasinda (en iyi en ustte, 0'dan i) ilk uyan yazinin agirligi DOGRUSAL n - i + 1 (n = satir
///     sayisi; varsayilan sirada Currency 10, Threads 9, Item Rarity 8, Unique 7 ... Gold 2); hicbirine uymayan 1.
///     16.11.2026'ya kadar 4^(n - i) idi: sira her seye baskindi, tek bir Currency 3 katli Unique kombinasyonunu geciyordu.
///     Kullanicinin istegi: "kombinasyon yapamiyorsak currency'e odaklanmaya gerek yok" - artik kopyayla gelen 3 kat
///     (ve kopya odulleri) sirada epey yukaridaki tek odulu geciyor (Unique x3 = 21 &gt; Currency x1 = 10).
/// </summary>
internal static class FatePlanner
{
    /// <summary>Kullanicinin onayladigi baslangic sirasi: Currency > Threads > Rarity > Unique > ...</summary>
    internal static readonly string[] DefaultRanking =
    {
        "Valuable Currency", "Currency", "Threads", "Item Rarity", "Unique", "Stacked Decks", "Maps", "Exceptional Gem",
        "Gems", "Gold",
    };

    private const int CopyMultiplier = 3;
    private const int MaxHand = 12;

    private static readonly Regex Progress = new(@"^(\d+)/(\d+) ", RegexOptions.CultureInvariant);

    private static readonly string[] Prefixes =
    {
        "Monster will drop an additional ", "Monster will drop additional ", "Monster will drop many additional ",
        "Monster will drop ", "Monster has a chance to drop additional ", "Monster has ",
    };

    internal sealed record Gain(string Text, int Count);

    /// <summary>Order: tiklama sirasi (her addan once ana kart). Mains: ana kartlar.</summary>
    internal sealed record Plan(IReadOnlyList<FateLabels.Card> Order, IReadOnlyList<FateLabels.Card> Mains, IReadOnlyList<Gain> Gains, double Score);

    /// <summary>Eldeki kartlardan en fazla <paramref name="picks"/> tanesinin en iyi secimi; el bossa null.</summary>
    internal static Plan? Best(IReadOnlyList<FateLabels.Card> hand, int picks, IReadOnlyList<string> ranking)
    {
        if (hand.Count == 0 || picks <= 0) { return null; }

        var phrases = ranking.Select(p => AltarLabels.Normalize(FateLabels.Clean(p))).Where(p => p.Length >= 3).ToArray();
        var n = Math.Min(hand.Count, MaxHand);
        Plan? best = null;
        for (var mask = 1; mask < 1 << n; mask++)
        {
            if (BitOperations.PopCount((uint)mask) > picks) { continue; }

            var chosen = Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0).Select(i => hand[i]).ToList();
            var plan = Evaluate(chosen, phrases);
            if (best is null || plan.Score > best.Score + 1e-9 ||
                (Math.Abs(plan.Score - best.Score) <= 1e-9 && plan.Order.Count < best.Order.Count))
            {
                best = plan;
            }
        }

        return best;
    }

    /// <summary>
    ///     "Select a Fate" panelinde hangi teklif alinmali: her teklif icin el + o kartla Invoke Fates'te yapilabilecek en iyi
    ///     secim (Best); en yuksek puanli teklif. Esitlikte (teklif plana girmiyorsa hepsi esit) kartin tek basina puani.
    ///     Ileride gelecek kartlar bilinmiyor: 16.11.2026 canli, uc kazan secim yapilmadan acildiginda ucu de AYNI teklifi
    ///     gosterdi; sonraki teklif ancak secimden sonra geliyor (ilk secimden sonra 4 kart + altta el).
    /// </summary>
    internal static (FateLabels.Card Pick, Plan Plan)? Draft(
        IReadOnlyList<FateLabels.Card> offers, IReadOnlyList<FateLabels.Card> hand, int picks, IReadOnlyList<string> ranking)
    {
        (FateLabels.Card Pick, Plan Plan, double Own)? best = null;
        var keep = hand.Take(MaxHand - 1).ToList();
        foreach (var offer in offers)
        {
            if (Best(keep.Append(offer).ToList(), picks, ranking) is not { } plan) { continue; }

            var own = Best(new[] { offer }, 1, ranking)?.Score ?? 0;
            if (best is null || plan.Score > best.Value.Plan.Score + 1e-9 ||
                (Math.Abs(plan.Score - best.Value.Plan.Score) <= 1e-9 && own > best.Value.Own + 1e-9))
            {
                best = (offer, plan, own);
            }
        }

        return best is { } b ? (b.Pick, b.Plan) : null;
    }

    private static Plan Evaluate(List<FateLabels.Card> chosen, string[] phrases)
    {
        var groups = chosen.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var copies = g.Count();
                var main = g.Select(m => (Card: m, Gains: Gains(m, copies)))
                    .Select(x => (x.Card, x.Gains, Score: x.Gains.Sum(r => r.Count * Weight(r.Text, phrases))))
                    .MaxBy(x => x.Score);
                return (main.Card, Others: g.Where(c => !ReferenceEquals(c, main.Card)).ToList(), main.Gains, main.Score);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var order = new List<FateLabels.Card>();
        foreach (var group in groups)
        {
            order.Add(group.Card);
            order.AddRange(group.Others);
        }

        var gains = groups.SelectMany(x => x.Gains)
            .GroupBy(r => r.Text, StringComparer.OrdinalIgnoreCase)
            .Select(x => new Gain(x.Key, x.Sum(r => r.Count)))
            .ToList();
        return new Plan(order, groups.Select(x => x.Card).ToList(), gains, groups.Sum(x => x.Score));
    }

    /// <summary>Ana kart, toplam <paramref name="copies"/> kopya ile ne verir.</summary>
    private static List<Gain> Gains(FateLabels.Card main, int copies)
    {
        var gains = new List<Gain>();
        foreach (var reward in main.Rewards)
        {
            var (text, need) = Parse(reward);
            if (need <= 1) { gains.Add(new Gain(text, copies >= 2 ? CopyMultiplier : 1)); }
            else if (copies >= need) { gains.Add(new Gain(text, 1)); }
        }

        return gains;
    }

    /// <summary>Odul yazisi ve kac kopya istedigi: "...\nTo claim this reward, invoke two copies of it" ya da "1/2 ...".</summary>
    internal static (string Text, int Copies) Parse(string reward)
    {
        var text = reward;
        var need = 1;
        if (Progress.Match(text) is { Success: true } m)
        {
            need = int.Parse(m.Groups[2].Value);
            text = text[m.Length..];
        }

        var claim = text.IndexOf("To claim this reward", StringComparison.OrdinalIgnoreCase);
        if (claim >= 0)
        {
            var tail = text[claim..];
            need = tail.Contains("three", StringComparison.OrdinalIgnoreCase) ? 3 : tail.Contains("two", StringComparison.OrdinalIgnoreCase) ? 2 : need;
            text = text[..claim].TrimEnd('\n', ' ');
        }

        return (text, need);
    }

    internal static double Weight(string text, string[] phrases)
    {
        var normal = AltarLabels.Normalize(text);
        for (var i = 0; i < phrases.Length; i++)
        {
            if (normal.Contains(phrases[i], StringComparison.OrdinalIgnoreCase)) { return phrases.Length - i + 1; }
        }

        return 1;
    }

    /// <summary>Kisa ad: ilk satir, "Monster will drop additional" gibi baslangic atilmis ("Rare Swords", "Threads").</summary>
    internal static string Short(string text)
    {
        var line = text.Split('\n')[0].Trim();
        foreach (var prefix in Prefixes)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return line[prefix.Length..]; }
        }

        return line;
    }

    /// <summary>"Rare Swords x3, Threads" bicimi.</summary>
    internal static string Describe(IEnumerable<Gain> gains) =>
        string.Join(", ", gains.Select(g => g.Count > 1 ? $"{Short(g.Text)} x{g.Count}" : Short(g.Text)));
}
