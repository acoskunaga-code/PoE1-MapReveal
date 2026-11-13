using System.Text;

namespace Poe2Map;

/// <summary>
///     Harita simgesi adlari oyunun KENDI tablosundan (MinimapIcons.dat, bellekte): satir sirasi -> Id. Ayar penceresinin
///     simge katalogu (catalog.txt) bundan yaziliyor; disaridan program dosyasi (ExileCore.dll) indirmek gerekmiyor.
///
///     PoE1 (14.11.2026, canli, `MapScan icontable`):
///
///         MinimapIcon bileseni +0x20 (MapIconReader.RowPointer)  tablonun bir satirina isaretci
///         satirlar                                               34 bayt aralikli (adres hizali degil)
///         satir +0x00                                            Id'nin UTF-16 metnine isaretci
///
///     Tablonun basi ve sonu: bilinen satirdan geriye ve ileriye, Id okunabildigi surece. Canli olcum: 708 satir, satir i
///     (0'dan) ExileApi'nin Icons.png'sindeki hucre i+1 ile 699 adin HEPSINDE ayni ad; oyunda 9 yeni simge daha var
///     (Augury*, Beyond*), resimde hucreleri yok. Yani indeks = satir + 1, resimle ayni sira.
///
///     Sinav (kendi kendini dogruluyor): en az 200 satir ve icinde "Waypoint" ile "NPC" olmali; degilse null, yazilmaz.
/// </summary>
internal static class IconTable
{
    private const int RowSize = 34;
    private const int MaxRows = 20_000;
    private const string ComponentName = "MinimapIcon";

    /// <summary>Alandaki ilk simge bileseninin satirindan butun tablo, (indeks, ad) olarak; bulunamazsa ya da sinavi gecmezse null.</summary>
    internal static List<(int Index, string Name)>? Read(IntPtr h, ulong area)
    {
        foreach (var offset in new[] { EntityReader.AwakeMap, EntityReader.SleepingMap })
        {
            if (EntityReader.ReadMapHeader(h, area + offset) is not { } map) { continue; }

            foreach (var (_, entity) in EntityReader.WalkMap(h, map, 5000))
            {
                if (EntityReader.ReadInfo(h, entity) is not { } info ||
                    !info.Components.TryGetValue(ComponentName, out var component)) { continue; }

                var row = PlayerChain.ReadPointer(h, component + MapIconReader.RowPointer);
                if (Id(h, row).Length == 0) { continue; }

                var names = Walk(h, row);
                var ok = names.Count >= 200 && names.Contains("Waypoint") && names.Contains("NPC");
                return ok ? names.Select((name, i) => (i + 1, name)).ToList() : null;
            }
        }

        return null;
    }

    /// <summary>Bilinen satirdan geriye tablonun basina, oradan ileriye sonuna kadar Id'ler.</summary>
    private static List<string> Walk(IntPtr h, ulong row)
    {
        var begin = row;
        for (var i = 0; i < MaxRows && Id(h, begin - RowSize).Length > 0; i++) { begin -= RowSize; }

        var names = new List<string>();
        for (var at = begin; names.Count < MaxRows; at += RowSize)
        {
            var id = Id(h, at);
            if (id.Length == 0) { break; }

            names.Add(id);
        }

        return names;
    }

    /// <summary>Satirin Id'si (satir +0 -> UTF-16; yalniz harf, rakam, _); okunamazsa "".</summary>
    private static string Id(IntPtr h, ulong row)
    {
        var p = new byte[8];
        if (row < 0x10000 || !EntityReader.Read(h, row, p)) { return ""; }

        var text = new byte[160];
        if (!EntityReader.Read(h, BitConverter.ToUInt64(p, 0), text)) { return ""; }

        var sb = new StringBuilder();
        for (var i = 0; i + 1 < text.Length; i += 2)
        {
            var c = (char)(text[i] | (text[i + 1] << 8));
            if (c == 0) { break; }
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') { return ""; }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
