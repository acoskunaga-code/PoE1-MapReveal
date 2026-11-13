namespace Poe2Map;

/// <summary>
///     Icons sekmesinde arama: yazi simgenin kimliginde ("RewardChestDelirium"), gorunen adinda ("Reward Chest
///     Delirium") ya da kategorisinin adinda geciyorsa simge listeye giriyor. Kategori adi tutarsa o kategorinin
///     tamami geliyor. Boylece "delir" Delirium kategorisinin hepsini VE baska kategorilerdeki delirium simgelerini
///     (ornegin Reward chests altindaki RewardChestDelirium) birlikte getiriyor. Buyuk / kucuk harf onemsiz.
/// </summary>
internal sealed partial class IconsPanel
{
    // Cok genis bir arama ("e") yuzlerce cetvel uretmesin: pencere donardi.
    private const int MaxResults = 150;

    private Panel? results;
    private int resultsHeight;

    /// <summary>Aramayi uygular; bos yazi kategorilere geri doner.</summary>
    internal void Search(string query)
    {
        query = query.Trim();

        if (this.results is { } old)
        {
            this.Forget(old);
            this.canvas.Controls.Remove(old);
            old.Dispose();
            this.results = null;
        }

        if (query.Length > 0)
        {
            var groups = new List<(string?, IReadOnlyList<IconCatalog.Entry>)>();
            var total = 0;
            var matched = 0;

            foreach (var category in this.catalog.Categories)
            {
                var whole = category.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                var hits = category.Entries.Where(e => whole || Matches(e, query)).ToList();
                if (hits.Count == 0) { continue; }

                matched += hits.Count;
                var room = MaxResults - total;
                if (room <= 0) { continue; }

                var shown = hits.Take(room).ToList();
                total += shown.Count;
                groups.Add(($"{category.Name} ({hits.Count})", shown));
            }

            if (matched > total) { groups.Add(($"... {matched - total} more, type more letters to narrow it down", Array.Empty<IconCatalog.Entry>())); }
            if (groups.Count == 0) { groups.Add(($"No icon matches \"{query}\"", Array.Empty<IconCatalog.Entry>())); }

            this.results = this.BuildRows(groups, out this.resultsHeight);
            this.canvas.Controls.Add(this.results);
        }

        this.AutoScrollPosition = Point.Empty;
        this.Relayout();
    }

    private static bool Matches(IconCatalog.Entry entry, string query) =>
        entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        entry.Label.Contains(query, StringComparison.OrdinalIgnoreCase);
}
