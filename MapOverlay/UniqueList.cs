namespace Poe2Map;

/// <summary>
///     Uniques sekmesinin listesi: data\unique-tiers.txt (`MapScan uniquetiers`, ucuncu taraf verisi, depoda yok).
///     "@grup" satirlari filtredeki sirayla gruplar; diger satirlar grup, ad, taban, sinif. Dosya yoksa null.
/// </summary>
internal sealed class UniqueList
{
    internal sealed record Group(string Id, string Title, bool Shown, List<Entry> Entries);

    internal sealed record Entry(string Name, string Base, string ItemClass);

    internal List<Group> Groups { get; } = new();

    internal static string FilePath => AppPaths.File("unique-tiers.txt");

    internal static UniqueList? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { return null; }

            var list = new UniqueList();
            var byId = new Dictionary<string, Group>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(FilePath))
            {
                if (line.Length == 0 || line[0] == '#') { continue; }

                var parts = line.Split('\t');
                if (parts[0] == "@grup" && parts.Length >= 4)
                {
                    var group = new Group(parts[1], parts[2], parts[3] == "show", new List<Entry>());
                    if (byId.TryAdd(group.Id, group)) { list.Groups.Add(group); }
                }
                else if (parts.Length >= 4 && byId.TryGetValue(parts[0], out var owner))
                {
                    owner.Entries.Add(new Entry(parts[1], parts[2], parts[3]));
                }
            }

            list.Groups.RemoveAll(g => g.Entries.Count == 0);
            return list.Groups.Count > 0 ? list : null;
        }
        catch
        {
            return null;
        }
    }
}
