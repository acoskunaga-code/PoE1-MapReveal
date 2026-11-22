using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     "Select a Fate"te alinacak karti, "Invoke Fates"te en iyi secimi ve TIKLAMA SIRASINI bulur (22.11.2026 yeniden
///     yazildi; kullanici: "fate kart secim onerileri cok kotu").
///
///     Oyunun kurali (14-18.11.2026 canli secim satiri okumalari):
///         - uste konan her kart odul simgelerini bir carpanla verir: tek kart 1; ayni adli 2 kartta once tiklanan 3,
///           sonraki 0; 3 kartta 1. ve 3. tiklanan 3, ortadaki 0 (3.30.0b Hotfix 3; canli 22.11.2026 06:44: uc
///           Houndmaster, oyunun toplami "6x greatly rarity, 1x many decks, 1/2 decks" = onerinin aynisi)
///         - duz odul simge sayisi kadar gelir
///         - "invoke N copies of it": "it" kart degil ODUL. Ayni odulun simgeleri (carpanla) uste konan butun kartlardan
///           toplanir, toplam / N kadar gelir. Iki Torchbearer + Stormcaller, "many Rare Maps" 2'li: ana kart 3 +
///           Stormcaller 1 = 4, iki kez geldi. Yani bir cift 3'lu odulu tek basina acar; uc farkli kart da birlikte acar.
///     Eski surum kopya odulunu ayni adli kart sayisiyla sayiyordu: bu kombinasyonlarin ikisini de hic onermiyordu.
///
///     Puan: gelen odullerin degeri (FateValues) + uste konan kart basina InvokedCard (boss rarity ve Viscera sansi;
///     cagrilmayan kart alandan cikinca kayboluyor). Select a Fate'te buna kalan kazanlarda ayni ad ya da ayni kombo
///     gelmesinin beklenen getirisi eklenir.
/// </summary>
internal static class FatePlanner
{
    private const int Tripled = 3;
    private const int MaxHand = 12;
    private const int MaxPicks = 4;
    private const double InvokedCard = 3;

    // Kazan sayisi panelden okunmuyor, haritada genelde 3-5. 25 Fate'ten 4'u teklif ediliyor; ayni kombo odulu TAHMIN.
    private const double Cauldrons = 4;
    private const double SameNameChance = 4.0 / 25;
    private const double SameRewardChance = 0.1;

    private static readonly Regex Progress = new(@"^(\d+)/(\d+) ", RegexOptions.CultureInvariant);

    private static readonly string[] Prefixes =
    {
        "Monster will drop an additional ", "Monster will drop additional ", "Monster will drop many additional ",
        "Monster will drop ", "Monster has a chance to drop additional ", "Monster has ",
    };

    internal sealed record Gain(string Text, int Count);

    /// <summary>Tamamlanmamis kopya odulu, oyunun "1/2 ..." satiri.</summary>
    internal sealed record Partial(string Text, int Have, int Need);

    /// <summary>Order: tiklama sirasi. Mains: odul veren kartlar (yanan kopya yok).</summary>
    internal sealed record Plan(
        IReadOnlyList<FateLabels.Card> Order,
        IReadOnlyList<FateLabels.Card> Mains,
        IReadOnlyList<Gain> Gains,
        IReadOnlyList<Partial> Partials,
        double Score);

    private readonly record struct Piece(int Key, int Need, string Text, double Value);

    private sealed record Entry(FateLabels.Card Card, string Name, Piece[] Pieces);

    /// <summary>Eldeki kartlardan en fazla <paramref name="picks"/> tanesinin en iyi sirali secimi; el bossa null.</summary>
    internal static Plan? Best(IReadOnlyList<FateLabels.Card> hand, int picks, FateValues values)
    {
        if (hand.Count == 0 || picks <= 0) { return null; }

        var (order, score) = Search(Entries(hand.Take(MaxHand), values), picks);
        return ToPlan(order, score);
    }

    /// <summary>
    ///     Select a Fate: her teklif icin el + o kartla yapilabilecek en iyi secim, arti sonraki kazanlardan beklenen.
    ///     Sonraki teklif ancak secimden sonra geliyor (16.11.2026 canli), o yuzden yalnizca ihtimal.
    /// </summary>
    internal static (FateLabels.Card Pick, Plan Plan)? Draft(
        IReadOnlyList<FateLabels.Card> offers, IReadOnlyList<FateLabels.Card> hand, int picks, FateValues values)
    {
        if (offers.Count == 0 || picks <= 0) { return null; }

        var held = hand.Take(MaxHand - 1).ToList();
        var all = Entries(held.Concat(offers), values);
        var before = all.Take(held.Count).ToList();
        (Entry Pick, List<Entry> Order, double Score, double Total, double Own)? best = null;

        foreach (var offer in all.Skip(held.Count))
        {
            var keep = before.Append(offer).ToList();
            var (order, score) = Search(keep, picks);
            var total = score + Future(keep, picks, score);

            // Esitlikte (kart plana girmiyorsa hepsi esit) kartin kendi odulleri.
            var own = offer.Pieces.Sum(p => p.Value / p.Need);
            if (best is not { } b || total > b.Total + 1e-9 || (Math.Abs(total - b.Total) <= 1e-9 && own > b.Own))
            {
                best = (offer, order, score, total, own);
            }
        }

        return best is { } found ? (found.Pick.Card, ToPlan(found.Order, found.Score)) : null;
    }

    /// <summary>Kalan kazanlarda gelebilecek en iyi tek seyin beklenen getirisi: bir adin kopyasi ya da bir kombo odulu.</summary>
    private static double Future(List<Entry> keep, int picks, double score)
    {
        var left = Math.Max(0.5, Cauldrons - keep.Count);
        double Chance(double each) => 1 - Math.Pow(1 - each, left);

        // Odulsuz kopya yeter: yanan kopyanin odulleri zaten sayilmiyor. Adsiz kart: kimseyle kopya olmayan kombo karti.
        var copies = keep.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => (Entry: keep[0] with { Name = name, Pieces = Array.Empty<Piece>() }, Chance: Chance(SameNameChance)));
        var combos = keep.SelectMany(e => e.Pieces).Where(p => p.Need > 1).Distinct()
            .Select(piece => (Entry: keep[0] with { Name = "", Pieces = new[] { piece } }, Chance: Chance(SameRewardChance)));

        var best = 0.0;
        foreach (var (entry, chance) in copies.Concat(combos))
        {
            best = Math.Max(best, chance * (Search(keep.Append(entry).ToList(), picks).Score - score));
        }

        return best;
    }

    /// <summary>Sira onemli (hangi kopyanin yandigi), butun sirali secimler denenir: 5 kart 3 yuvada 60.</summary>
    private static (List<Entry> Order, double Score) Search(List<Entry> cards, int picks)
    {
        var slots = Math.Min(Math.Min(picks, MaxPicks), cards.Count);
        var counts = new int[cards.SelectMany(c => c.Pieces).Select(p => p.Key + 1).DefaultIfEmpty(0).Max()];
        var order = new Entry[slots];
        var used = new bool[cards.Count];
        var bestOrder = new List<Entry>();
        var bestScore = double.NegativeInfinity;

        void Try(int depth)
        {
            if (depth == slots)
            {
                var score = ScoreOf(order, counts);
                if (score > bestScore + 1e-9) { (bestOrder, bestScore) = (order.ToList(), score); }
                return;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                if (used[i]) { continue; }

                used[i] = true;
                order[depth] = cards[i];
                Try(depth + 1);
                used[i] = false;
            }
        }

        Try(0);
        return (bestOrder, bestScore);
    }

    private static double ScoreOf(Entry[] order, int[] counts)
    {
        Array.Clear(counts);
        var score = order.Length * InvokedCard;
        for (var i = 0; i < order.Length; i++)
        {
            var times = Multiplier(order, i);
            if (times == 0) { continue; }

            foreach (var piece in order[i].Pieces)
            {
                var before = counts[piece.Key];
                counts[piece.Key] += times;
                score += ((counts[piece.Key] / piece.Need) - (before / piece.Need)) * piece.Value;
            }
        }

        return score;
    }

    private static int Multiplier(IReadOnlyList<Entry> order, int index)
    {
        var name = order[index].Name;
        var (earlier, copies) = (0, 0);
        for (var i = 0; i < order.Count; i++)
        {
            if (!string.Equals(order[i].Name, name, StringComparison.OrdinalIgnoreCase)) { continue; }

            copies++;
            if (i < index) { earlier++; }
        }

        return copies == 1 ? 1 : earlier == 1 ? 0 : Tripled;
    }

    private static Plan ToPlan(List<Entry> order, double score)
    {
        var mains = new List<FateLabels.Card>();
        var totals = new Dictionary<Piece, int>();
        for (var i = 0; i < order.Count; i++)
        {
            var times = Multiplier(order, i);
            if (times == 0) { continue; }

            mains.Add(order[i].Card);
            foreach (var piece in order[i].Pieces) { totals[piece] = totals.GetValueOrDefault(piece) + times; }
        }

        // Ayni yazi hem duz hem kopya odulu olabilir (Currency Items); kutuda tek satir, degerliden ucuza.
        var gains = totals.Where(t => t.Value >= t.Key.Need)
            .OrderByDescending(t => t.Value / t.Key.Need * t.Key.Value)
            .GroupBy(t => t.Key.Text, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Gain(g.Key, g.Sum(t => t.Value / t.Key.Need)))
            .ToList();
        var partials = totals.Where(t => t.Value % t.Key.Need != 0)
            .Select(t => new Partial(t.Key.Text, t.Value % t.Key.Need, t.Key.Need))
            .ToList();
        return new Plan(order.Select(e => e.Card).ToList(), mains, gains, partials, score);
    }

    /// <summary>Odul yazilari bir kez cozulup fiyatlanir; ayni yazi + ayni N tek anahtar.</summary>
    private static List<Entry> Entries(IEnumerable<FateLabels.Card> cards, FateValues values)
    {
        var known = new Dictionary<(string Text, int Need), Piece>();
        return cards.Select(card => new Entry(card, card.Name, card.Rewards.Select(reward =>
        {
            var (text, need) = Parse(reward);
            var key = (text.ToLowerInvariant(), need);
            if (!known.TryGetValue(key, out var piece)) { known[key] = piece = new Piece(known.Count, need, text, values.Of(text, need)); }

            return piece;
        }).ToArray())).ToList();
    }

    /// <summary>Odul yazisi ve kac simge istedigi: "...\nTo claim this reward, invoke two copies of it" ya da "1/2 ...".</summary>
    internal static (string Text, int Need) Parse(string reward)
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

        return (text, Math.Max(1, need));
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

    /// <summary>"Currency Items x3, Threads (1/3 Valuable Currency Items)" bicimi.</summary>
    internal static string Describe(Plan plan)
    {
        var won = string.Join(", ", plan.Gains.Select(g => g.Count > 1 ? $"{Short(g.Text)} x{g.Count}" : Short(g.Text)));
        var waiting = string.Join(", ", plan.Partials.Select(p => $"{p.Have}/{p.Need} {Short(p.Text)}"));
        return waiting.Length == 0 ? won : won.Length == 0 ? waiting : $"{won} ({waiting})";
    }
}
