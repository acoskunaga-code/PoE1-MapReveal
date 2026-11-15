namespace Poe2Map;

/// <summary>
///     Acilan Djinn's Cache: harita simgesi olmadigi icin oyunun "gizle" bayragi yok, acildigini kendi bilesenlerinden
///     okumak gerekiyor. 14.11.2026: bayrak HENUZ BILINMIYOR. Bulmak icin her sandigin Chest / Targetable /
///     StateMachine baytlari ilk goruldugunde gunluge yaziliyor ve sonra degisen her 4 baytlik alan (sandik basina en
///     fazla 24 satir) zamaniyla yaziliyor; sandik listeden cikinca o da yaziliyor. Kullanici bir sandik acinca hangi
///     alanin degistigi gunlukte gorunur, kural ondan sonra <see cref="CacheOpened"/> icine yazilacak.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly (string Name, int Size)[] CacheParts = { ("Chest", 0x60), ("Targetable", 0x60), ("StateMachine", 0x40) };
    private const int CacheLogLimit = 24;

    private readonly Dictionary<ulong, byte[]> cacheStates = new();
    private readonly Dictionary<ulong, int> cacheLogLines = new();
    private readonly Dictionary<ulong, string> cacheNames = new();
    private HashSet<ulong> cachesSeenLastTurn = new();
    private HashSet<ulong> cachesSeenThisTurn = new();

    /// <summary>Sandik acilmis mi. Su an hep false; bayrak bulunana kadar sadece durumu gunluge yaziyor.</summary>
    private bool CacheOpened(IntPtr h, ulong entity, EntityReader.EntityInfo info, CacheReader.Marker marker)
    {
        this.cachesSeenThisTurn.Add(entity);

        var now = new byte[CacheParts.Sum(p => p.Size)];
        var at = 0;
        foreach (var (name, size) in CacheParts)
        {
            if (info.Components.TryGetValue(name, out var component))
            {
                var part = new byte[size];
                if (EntityReader.Read(h, component, part)) { Buffer.BlockCopy(part, 0, now, at, size); }
            }

            at += size;
        }

        if (!this.cacheStates.TryGetValue(entity, out var before))
        {
            this.cacheStates[entity] = now;
            this.cacheNames[entity] = marker.Label;
            CrashLog.Write($"djinn cache goruldu: \"{marker.Label}\" {info.Path} varlik {entity:X} {StateText(now)}", null);
            return false;
        }

        for (var o = 0; o + 4 <= now.Length; o += 4)
        {
            var a = BitConverter.ToUInt32(before, o);
            var b = BitConverter.ToUInt32(now, o);
            if (a == b) { continue; }

            var lines = this.cacheLogLines.GetValueOrDefault(entity);
            if (lines >= CacheLogLimit) { break; }

            this.cacheLogLines[entity] = lines + 1;
            CrashLog.Write($"djinn cache degisti: \"{marker.Label}\" varlik {entity:X} {PartName(o)} {a:X8} -> {b:X8}", null);
        }

        this.cacheStates[entity] = now;
        return false;
    }

    /// <summary>Tur sonunda: bir onceki turda gorulup bu turda gorulmeyen sandiklar (acilinca listeden cikiyor mu?).</summary>
    private void EndCacheTurn()
    {
        foreach (var gone in this.cachesSeenLastTurn.Where(e => !this.cachesSeenThisTurn.Contains(e)))
        {
            CrashLog.Write($"djinn cache listeden cikti: \"{this.cacheNames.GetValueOrDefault(gone, "?")}\" varlik {gone:X}", null);
        }

        this.cachesSeenLastTurn = this.cachesSeenThisTurn;
        this.cachesSeenThisTurn = new HashSet<ulong>();
    }

    /// <summary>Alan degisince: eski alanin sandiklari "listeden cikti" diye yazilmasin.</summary>
    private void ResetCacheWatch()
    {
        this.cacheStates.Clear();
        this.cacheLogLines.Clear();
        this.cacheNames.Clear();
        this.cachesSeenLastTurn = new HashSet<ulong>();
        this.cachesSeenThisTurn = new HashSet<ulong>();
    }

    private static string PartName(int offset)
    {
        foreach (var (name, size) in CacheParts)
        {
            if (offset < size) { return $"{name}+{offset:X2}"; }
            offset -= size;
        }

        return $"?+{offset:X2}";
    }

    private static string StateText(byte[] state)
    {
        var parts = new List<string>();
        var at = 0;
        foreach (var (name, size) in CacheParts)
        {
            var words = Enumerable.Range(0, size / 4).Select(k => BitConverter.ToUInt32(state, at + (k * 4)).ToString("X8"));
            parts.Add($"{name}[{string.Join(' ', words)}]");
            at += size;
        }

        return string.Join(' ', parts);
    }
}
