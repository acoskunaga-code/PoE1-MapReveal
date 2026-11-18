namespace Poe2Map;

/// <summary>
///     Yerdeki esyanin oyundaki yazisi (ekrandaki "Heavy Belt" kutusu). 18.11.2026 canli (`MapScan groundlabels 1/6/0 0
///     1/6`): yazilar arayuz kok/1/6/0'in cocuklari, ama yazi ogesinde varliga isaretci yok. Esleme kok/1/6 ogesinde:
///
///         +0x550 -> agac: dugum +0x0 ve +0x8 alt dugumler, +0x10 WorldItem varligi, +0x18 yazi ogesi
///         (+0x510'da ters yonde ayni agac: +0x10 yazi, +0x18 varlik)
///
///     Dugumler sol/sag isaretcileriyle geziliyor; dongu ihtimaline karsi ziyaret kumesi ve ust sinir var.
/// </summary>
internal static class GroundLabelMap
{
    internal static readonly int[] ElementPath = { 1, 6 };
    internal static ulong EntityToLabel = 0x550;

    /// <summary>WorldItem varligi -> yazi ogesi. <paramref name="wanted"/> verilirse yalniz onlar (hepsi bulununca durur).</summary>
    internal static Dictionary<ulong, ulong> Read(IntPtr h, ulong element, IReadOnlySet<ulong>? wanted = null, int max = 6000)
    {
        var result = new Dictionary<ulong, ulong>();
        var head = PlayerChain.ReadPointer(h, element + EntityToLabel);
        if (!PlayerChain.LooksLikePointer(head)) { return result; }

        var visited = new HashSet<ulong> { head };
        var stack = new Stack<ulong>();
        stack.Push(head);
        var node = new byte[0x20];
        while (stack.Count > 0 && visited.Count < max)
        {
            var at = stack.Pop();
            if (!EntityReader.Read(h, at, node)) { continue; }

            var entity = BitConverter.ToUInt64(node, 0x10);
            var label = BitConverter.ToUInt64(node, 0x18);
            if (at != head && PlayerChain.LooksLikePointer(entity) && PlayerChain.LooksLikePointer(label) &&
                (wanted is null || wanted.Contains(entity)))
            {
                result[entity] = label;
                if (wanted is not null && result.Count == wanted.Count) { break; }
            }

            foreach (var next in new[] { BitConverter.ToUInt64(node, 0), BitConverter.ToUInt64(node, 8) })
            {
                if (PlayerChain.LooksLikePointer(next) && visited.Add(next)) { stack.Push(next); }
            }
        }

        return result;
    }
}
