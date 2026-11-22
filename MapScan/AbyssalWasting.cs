using System.Collections.Concurrent;
using System.Text;

namespace Poe2Map;

/// <summary>
///     Abyssal Wasting zemini ve onu birakan canavar (kullanici 20.11.2026: "bu efekt yada bu efekti yere birakan monster
///     belirdiginde bana yerini gosterebilirmisin"). Canli olcum 20.11.2026, Abyss'li haritada:
///
///       - zemin: "Metadata/Monsters/AbyssTwoLeague/AbyssalGroundDaemon" varligi (Life + DiesAfterTime; sure bitince yok
///         oluyor). Uzerinde duran oyuncuya "abyssal_wasting" laneti: BuffDefinitions aciklamasi "Your Damage with Hits
///         is Unlucky" (AbyssalWasting satiri "Curse" turunde, kure gorseli curses_02/orbs/abyssal.ao).
///       - kaynak: canavar modu "MonsterModAbyssalGround" (Mods.dat Id; gorunen adi "Wasting Well"). Mod listesi
///         ObjectMagicProperties +0x150 (bas, son), eleman 0x38 bayt, eleman +0x28 = Mods.dat satiri, satir +0 = Id.
///         (`MapScan monstermods`: "MonsterRare1".. ve ozel modlar hep +0x28'de.)
/// </summary>
internal static class AbyssalWasting
{
    internal const string GroundPath = "Metadata/Monsters/AbyssTwoLeague/AbyssalGroundDaemon";
    private const string ModId = "MonsterModAbyssalGround";

    private const ulong ModVector = 0x150;
    private const int ModSize = 0x38;
    private const int ModRow = 0x28;
    private const int MaxMods = 64;

    // Mods.dat satiri -> Wasting Well mi. Satirlar oyun boyunca sabit, Id bir kez okunuyor.
    private static readonly ConcurrentDictionary<ulong, bool> rows = new();

    // Canli tani 20.11.2026 03:01 (OverlayForm.Wasting.cs, "abyssal wasting yakinindakiler"): iki ayri zemin, ikisi de
    // bir Tarhulk'un ustunde (0 ve 6 birim) belirdi; Tarhulk'ta Wasting Well modu yoktu. Zemini Tarhulk'un kendisi birakiyor.
    internal const string TarhulkPath = "Metadata/Monsters/AbyssTwoLeague/Tarhulk";

    internal static bool IsGround(EntityReader.EntityInfo info) => info.Path.StartsWith(GroundPath, StringComparison.Ordinal);

    internal static bool IsTarhulk(EntityReader.EntityInfo info) => info.Path.StartsWith(TarhulkPath, StringComparison.Ordinal);

    /// <summary>
    ///     Canavarin modlarindan biri Wasting Well mi (MonsterModAbyssalGround). Mod listesi okunamadi ya da bos ise null:
    ///     yeni doganin listesi ilk turda henuz dolmamis olabilir, cagiran sonucu onbellege almasin.
    /// </summary>
    internal static bool? HasWellMod(IntPtr h, EntityReader.EntityInfo info)
    {
        if (!info.Components.TryGetValue("ObjectMagicProperties", out var omp)) { return false; }

        var vector = new byte[16];
        if (!EntityReader.Read(h, omp + ModVector, vector)) { return null; }

        var first = BitConverter.ToUInt64(vector, 0);
        var last = BitConverter.ToUInt64(vector, 8);
        if (last <= first || last - first > ModSize * MaxMods || (last - first) % ModSize != 0) { return null; }

        var list = new byte[(int)(last - first)];
        if (!EntityReader.Read(h, first, list)) { return null; }

        for (var m = 0; m + ModSize <= list.Length; m += ModSize)
        {
            var row = BitConverter.ToUInt64(list, m + ModRow);
            if (row is < 0x10000 or > 0x7FFFFFFFFFFF) { continue; }
            if (rows.GetOrAdd(row, r => RowId(h, r) == ModId)) { return true; }
        }

        return false;
    }

    /// <summary>
    ///     Canavarin "MonsterRare1".."MonsterMagic" disindaki mod Id'leri (tani: zemini kim birakiyor). Gunluk icin,
    ///     sadece zemin ilk belirdiginde cagriliyor.
    /// </summary>
    internal static IEnumerable<string> SpecialMods(IntPtr h, EntityReader.EntityInfo info)
    {
        if (!info.Components.TryGetValue("ObjectMagicProperties", out var omp)) { yield break; }

        var vector = new byte[16];
        if (!EntityReader.Read(h, omp + ModVector, vector)) { yield break; }

        var first = BitConverter.ToUInt64(vector, 0);
        var last = BitConverter.ToUInt64(vector, 8);
        if (last <= first || last - first > ModSize * MaxMods || (last - first) % ModSize != 0) { yield break; }

        var list = new byte[(int)(last - first)];
        if (!EntityReader.Read(h, first, list)) { yield break; }

        for (var m = 0; m + ModSize <= list.Length; m += ModSize)
        {
            var id = RowId(h, BitConverter.ToUInt64(list, m + ModRow));
            if (id.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(id, @"^Monster(Rare|Magic|Unique)\d*$")) { yield return id; }
        }
    }

    private static string RowId(IntPtr h, ulong row)
    {
        var slot = new byte[8];
        if (!EntityReader.Read(h, row, slot)) { return ""; }

        var text = BitConverter.ToUInt64(slot, 0);
        var raw = new byte[64];
        if (text is < 0x10000 or > 0x7FFFFFFFFFFF || !EntityReader.Read(h, text, raw)) { return ""; }

        var s = Encoding.Unicode.GetString(raw);
        var end = s.IndexOf('\0');
        return end >= 0 ? s[..end] : s;
    }
}
