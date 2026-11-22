using System.Collections.Concurrent;
using System.Text;

namespace Poe2Map;

/// <summary>
///     Henuz acilmamis Legion canavarlari haritada cizilmesin (kullanici 19.11.2026: "legion monsterlari spawn olmadi
///     henuz ama gosteriyor"; 21.11.2026: "sadece legion monsterlarinin spawn olmadigi surece gostermeme yapabilirmisin",
///     ilk surum Eldritch canavarlarini da gizlemisti). Bu yuzden sinav yalnizca Metadata/Monsters/LegionLeague/
///     yolundaki canavarlara uygulaniyor; baska hicbir canavar buff yuzunden gizlenmiyor.
///
///     Canli olcum 19.11.2026 (`MapScan buffs Metadata/Monsters/LegionLeague`): acilmamis Legion'daki canavarin Buffs
///     bileseninde "hidden_monster", "frozen_in_time" ve "visual_display_buff" var; Legion acilinca donma kalkiyor ve
///     canavar normal cizilir.
///
///     Yerlesim (BuffProbe ile ayni): Buffs +0x160 = buff isaretcileri vektoru (bas, son); buff +0x08 = BuffDefinitions
///     satiri; satir +0x00 = Id metni (UTF-16). Satir adresleri oyun boyunca sabit: Id bir kez okunup onbellekte tutuluyor,
///     her turda Legion canavari basina yalnizca vektor ve buff isaretcileri okunuyor.
/// </summary>
internal static class HiddenMonsters
{
    internal const string LegionPrefix = "Metadata/Monsters/LegionLeague/";
    internal static ulong BuffVector = 0x160;
    private const int BuffDefinition = 0x08;
    private const int MaxBuffs = 64;

    [Flags]
    internal enum Marks
    {
        None = 0,
        Hidden = 1, // "hidden_monster"
        Frozen = 2, // "frozen_in_time"
    }

    // BuffDefinitions satiri -> bu buff hangi isaret.
    private static readonly ConcurrentDictionary<ulong, Marks> rows = new();

    internal static bool IsLegion(EntityReader.EntityInfo info) => info.Path.StartsWith(LegionPrefix, StringComparison.Ordinal);

    /// <summary>
    ///     Acilmamis Legion canavari mi (yalnizca Legion yolundakiler; digerleri hep false). Kullanici 21.11.2026: "legion a
    ///     tikladigimda gorunmeye baslayabilir mi". Canli izleme 05:23-05:26 (`MapScan hidden watch`, iki Legion acildi):
    ///     tiklamadan once hepsi Hidden+Frozen; tiklayinca hepsi yalniz Frozen (140); sure islerken teker teker yalniz
    ///     Hidden'a geciyorlar; sure bitince buff'siz ve savasiyorlar. Yani yalnizca ikisi BIRDEN acilmamis demek: tek
    ///     basina Hidden (sure icinde) ya da Frozen gizlemiyor, canavar tiklamadan sonra hep cizilir.
    /// </summary>
    internal static bool IsUnopenedLegion(IntPtr h, EntityReader.EntityInfo info) =>
        IsLegion(info) && info.Components.TryGetValue("Buffs", out var buffs) && Read(h, buffs) == (Marks.Hidden | Marks.Frozen);

    /// <summary>
    ///     Legion'a tiklandiktan sonra henuz vurulup uyandirilmamis canavar mi (kullanici 22.11.2026: "bana aktif
    ///     etmedigimi gosterebilirmisin"; hepsi vurulunca sure beklenmeden canlaniyorlar). Yalniz Frozen: tiklamadan once
    ///     Hidden+Frozen, sure icinde teker teker yalniz Hidden'a geciyorlar (21.11 05:23 izlemesi), sure bitince buff'siz.
    /// </summary>
    internal static bool IsUnhitLegion(IntPtr h, EntityReader.EntityInfo info) =>
        IsLegion(info) && info.Components.TryGetValue("Buffs", out var buffs) && Read(h, buffs) == Marks.Frozen;

    /// <summary>Canavar "hidden_monster" ya da "frozen_in_time" buff'i tasiyor mu (yol sinavi yok; MapScan hidden icin).</summary>
    internal static bool HasHiddenBuff(IntPtr h, ulong buffs) => Read(h, buffs) != Marks.None;

    /// <summary>Canavarin tasidigi gizleme / donma buff'lari.</summary>
    internal static Marks Read(IntPtr h, ulong buffs)
    {
        var vector = new byte[16];
        if (!EntityReader.Read(h, buffs + BuffVector, vector)) { return Marks.None; }

        var first = BitConverter.ToUInt64(vector, 0);
        var last = BitConverter.ToUInt64(vector, 8);
        if (!PlayerChain.LooksLikePointer(first) || last <= first || (last - first) % 8 != 0 || last - first > 8 * MaxBuffs) { return Marks.None; }

        var list = new byte[(int)(last - first)];
        if (!EntityReader.Read(h, first, list)) { return Marks.None; }

        var marks = Marks.None;
        var slot = new byte[8];
        for (var i = 0; i + 8 <= list.Length; i += 8)
        {
            var buff = BitConverter.ToUInt64(list, i);
            if (!PlayerChain.LooksLikePointer(buff) || !EntityReader.Read(h, buff + BuffDefinition, slot)) { continue; }

            // Dat satirlari sikisik: satir ve Id isaretcisi 8'e hizali olmayabiliyor (LooksLikePointer hizali istiyor).
            var row = BitConverter.ToUInt64(slot, 0);
            if (!InRange(row)) { continue; }

            marks |= rows.GetOrAdd(row, r => RowId(h, r) switch
            {
                "hidden_monster" => Marks.Hidden,
                "frozen_in_time" => Marks.Frozen,
                _ => Marks.None,
            });
        }

        return marks;
    }

    /// <summary>Canavarin butun buff Id'leri (LegionProbe icin).</summary>
    internal static List<string> BuffIds(IntPtr h, ulong buffs) => BuffList(h, buffs).Select(b => b.Id).ToList();

    /// <summary>Canavarin butun buff'lari: buff nesnesinin adresi ve Id'si (LegionProbe icin).</summary>
    internal static List<(ulong Buff, string Id)> BuffList(IntPtr h, ulong buffs)
    {
        var ids = new List<(ulong, string)>();
        var vector = new byte[16];
        if (!EntityReader.Read(h, buffs + BuffVector, vector)) { return ids; }

        var first = BitConverter.ToUInt64(vector, 0);
        var last = BitConverter.ToUInt64(vector, 8);
        if (!PlayerChain.LooksLikePointer(first) || last <= first || (last - first) % 8 != 0 || last - first > 8 * MaxBuffs) { return ids; }

        var list = new byte[(int)(last - first)];
        if (!EntityReader.Read(h, first, list)) { return ids; }

        var slot = new byte[8];
        for (var i = 0; i + 8 <= list.Length; i += 8)
        {
            var buff = BitConverter.ToUInt64(list, i);
            if (!PlayerChain.LooksLikePointer(buff) || !EntityReader.Read(h, buff + BuffDefinition, slot)) { continue; }

            var row = BitConverter.ToUInt64(slot, 0);
            if (InRange(row)) { ids.Add((buff, RowId(h, row))); }
        }

        return ids;
    }

    private static string RowId(IntPtr h, ulong row)
    {
        var slot = new byte[8];
        if (!EntityReader.Read(h, row, slot)) { return ""; }

        var text = BitConverter.ToUInt64(slot, 0);
        var raw = new byte[64];
        if (!InRange(text) || !EntityReader.Read(h, text, raw)) { return ""; }

        // UTF-16 (ikinci bayt 0) ya da ASCII.
        var s = raw[1] == 0 ? Encoding.Unicode.GetString(raw) : Encoding.ASCII.GetString(raw);
        var end = s.IndexOf('\0');
        return end >= 0 ? s[..end] : s;
    }

    private static bool InRange(ulong v) => v >= 0x10000 && v <= 0x7FFFFFFFFFFF;
}
