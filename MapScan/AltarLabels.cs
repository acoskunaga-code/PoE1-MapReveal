using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Eldritch altar etiketleri: altarin ustunde oyunun gosterdigi iki secenek kutusu ("Map boss gains:",
///     "Eldritch Minions gain:", "Player gains:" ile baslayan metinler). Kutularin metni ve ekrandaki yeri oyunun
///     arayuz agacindan okunuyor; altar varliginin kendisinde secenek yok.
///
///     PoE1 (12.11.2026, canli, MapScan uitext "gains:"):
///
///         etiket kabi                  kok/1/6/0 (yerdeki etiketler; her cocuk bir varligin etiketi)
///         altar etiketi                kabin cocugu; cocuk 0 = ust secenek, cocuk 1 = alt secenek
///         secenek -> cocuk 1           metin ogesi; ekrandaki kutusu secenek kutusunun tamami
///         oge +0x338                   metin, std::wstring: "&lt;valuedefault&gt;{Player gains:}\n...satirlar..."
///
///     Ekrandaki yer UiFinder.ElementPosition ile; olculen kutular ekran goruntusundekilerle piksel piksel ayni
///     (ornek: "Player gains" kutusu 449,424 boyut 714x69, istemci 1920x1017). Yamadan sonra `MapScan uitext gains:`
///     ayni satirlari yeniden basar; +0x338 kayarsa metin baska offsette cikar.
/// </summary>
internal static class AltarLabels
{
    // 27.10/12.11: 0x338. 13.11.2026 yamasi: 0x358, canli (14.11.2026 `MapScan uitext Fate`: "Select a Fate", kart adlari ve
    // odul yazilari hep +0x358'de). Statik tahmin 0x360'ti, yanlisti; v3.30 altar etiketlerini o yuzden okuyamiyordu.
    // 19.11.2026 (3.30.0c): 0x360, canli (`MapScan uitext`: "Lush Hideout", "Guild Stash" ... hep +0x360).
    internal const int TextOffset = 0x360;

    private const int OffSelf = 0xB8;
    private const int OffChildren = 0xC0;
    private const int MaxChildren = 4096;

    // "<valuedefault>{Player gains:}" -> "Player"; "Eldritch Minions gain:" de ayni kalipta.
    private static readonly Regex Header = new(@"^<[^>]*>\{\s*(?<who>[^}]*?)\s+gains?:\s*\}", RegexOptions.CultureInvariant);

    /// <summary>Bir secenek kutusu: metin ogesinin adresi, kimin kazandigi ("Player", "Map boss", ...) ve metin.</summary>
    internal readonly record struct Option(ulong Element, string Target, string Text);

    private static readonly Regex Tags = new(@"<[^>]*>|[{}]", RegexOptions.CultureInvariant);

    // Sayi ve aralik: "2.4", "(1.6—3.2)", "(2-4)", "16" -> "#". Kullanici poedb'den araligi kopyalayabilsin,
    // oyunda ise tek sayi yaziyor: ikisi de "#% chance to drop an additional Divine Orb" olur.
    private static readonly Regex Numbers = new(
        @"\(?\d+(?:[.,]\d+)?(?:\s*[-–—]+\s*\d+(?:[.,]\d+)?)?\)?", RegexOptions.CultureInvariant);

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Karsilastirma bicimi: oyunun bicim etiketleri ("&lt;enchanted&gt;{...}") ve suslu parantezler atiliyor,
    ///     sayilar ve araliklar "#" oluyor, bosluklar teke iniyor. Aranan yazi da metin de bu bicime getirilip
    ///     buyuk/kucuk harfe bakmadan iceriyor mu diye bakiliyor.
    /// </summary>
    internal static string Normalize(string text) =>
        Spaces.Replace(Numbers.Replace(Tags.Replace(text, " "), "#"), " ").Trim();

    /// <summary>Metin bir altar secenegi mi; oyleyse kazanan ("Player", "Map boss", "Eldritch Minions").</summary>
    internal static string? TargetOf(string text)
    {
        var m = Header.Match(text);
        return m.Success ? m.Groups["who"].Value : null;
    }

    /// <summary>Arayuz agacinin koku: verilen ogeden (buyuk harita) ebeveyn zinciri boyunca en uste.</summary>
    internal static ulong RootOf(IntPtr h, ulong element)
    {
        var root = element;
        for (var i = 0; i < 64 && UiFinder.Reread(h, root) is { ParentPtr: not 0 } e && UiFinder.Reread(h, e.ParentPtr) is not null; i++)
        {
            root = e.ParentPtr;
        }

        return root;
    }

    /// <summary>
    ///     Yavas arama (arka planda, birkac saniye): agaci kokten gezip bir altar secenek metni bulur ve etiket kabini
    ///     (metin -> secenek -> etiket -> kap, uc ebeveyn yukari) dondurur. Ekranda altar etiketi yoksa 0.
    /// </summary>
    // Metin ogesi kokten 6 kat asagida (kok/1/6/0/etiket/secenek/metin); 7'den derine inmek gereksiz ve butun agaci
    // (57 bin oge) okumak demek. Etiket yokken arama bes saniyede bir tekrarlandigi icin sinir onemli.
    internal static ulong FindContainer(IntPtr h, ulong root, int maxDepth = 7, int maxElements = 60_000)
    {
        var queue = new Queue<(ulong Address, int Depth)>();
        var seen = new HashSet<ulong> { root };
        queue.Enqueue((root, 0));
        var buf = new byte[TextOffset + 0x20];

        while (queue.Count > 0 && seen.Count < maxElements)
        {
            var (address, depth) = queue.Dequeue();
            if (!EntityReader.Read(h, address, buf) || BitConverter.ToUInt64(buf, OffSelf) != address) { continue; }

            if (TargetOf(EntityReader.ReadWString(h, buf, TextOffset)) is not null)
            {
                var container = Ancestor(h, address, 3);
                if (container != 0) { return container; }
            }

            if (depth >= maxDepth) { continue; }

            foreach (var child in Children(h, buf))
            {
                if (seen.Add(child)) { queue.Enqueue((child, depth + 1)); }
            }
        }

        return 0;
    }

    /// <summary>
    ///     Hizli tarama (saniyede birkac kez): kabin her cocugunun iki kat altina bakip altar seceneklerini toplar.
    ///     Kap artik gecerli degilse (kendi adresini tutmuyor) null: yeniden aranmali.
    /// </summary>
    internal static List<Option>? Scan(IntPtr h, ulong container)
    {
        var buf = new byte[TextOffset + 0x20];
        if (!EntityReader.Read(h, container, buf) || BitConverter.ToUInt64(buf, OffSelf) != container) { return null; }

        var result = new List<Option>();
        foreach (var label in Children(h, buf))
        {
            if (!EntityReader.Read(h, label, buf)) { continue; }

            foreach (var option in Children(h, buf).Take(4))
            {
                if (!EntityReader.Read(h, option, buf)) { continue; }

                foreach (var text in Children(h, buf).Take(4))
                {
                    if (!EntityReader.Read(h, text, buf) || BitConverter.ToUInt64(buf, OffSelf) != text) { continue; }

                    var value = EntityReader.ReadWString(h, buf, TextOffset);
                    if (TargetOf(value) is { } who) { result.Add(new Option(text, who, value)); }
                }
            }
        }

        return result;
    }

    /// <summary>
    ///     Etiket kabinin kokten cocuk sirasi yolu ("1/6/0"). Bir kez metinle bulununca kaydediliyor; sonra arayuz
    ///     yeniden kurulsa da kap bu yoldan aninda bulunuyor, ekranda altar etiketi olmasina gerek kalmadan. Ilk
    ///     altarin cercevesi bu yuzden artik gecikmiyor (once her alanda etiket goruldukten sonra araniyordu).
    /// </summary>
    internal static string PathFile => AppPaths.File("altar-labels.txt");

    internal static int[]? LoadPath() => LoadPath(PathFile);

    /// <summary>Kokten cocuk sirasi yolu dosyasi: # ile baslamayan ilk satir ("1/6/0"). Yoksa ya da bozuksa null.</summary>
    internal static int[]? LoadPath(string file)
    {
        try
        {
            if (!File.Exists(file)) { return null; }

            var line = File.ReadLines(file).FirstOrDefault(l => l.Length > 0 && l[0] != '#') ?? "";
            var parts = line.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var path = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out path[i]) || path[i] < 0) { return null; }
            }

            return path.Length is > 0 and < 16 ? path : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void SavePath(int[] path) => SavePath(
        PathFile,
        path,
        "# Eldritch altar etiket kabinin arayuz kokunden cocuk sirasi yolu. Overlay metinle bulup yaziyor;",
        "# yamadan sonra tutmazsa kendiliginden yeniden bulur ve bu satiri gunceller.");

    internal static void SavePath(string file, int[] path, params string[] header)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, header.Append(string.Join('/', path)));
        }
        catch
        {
            // Yazilamazsa bir sonraki acilista yeniden aranir; zararsiz.
        }
    }

    /// <summary>Ogenin kokten cocuk sirasi yolu; koke ulasilamazsa null.</summary>
    internal static int[]? PathOf(IntPtr h, ulong root, ulong element)
    {
        var path = new List<int>();
        var cursor = element;
        var buf = new byte[TextOffset + 0x20];
        for (var i = 0; i < 32 && cursor != root; i++)
        {
            if (UiFinder.Reread(h, cursor) is not { ParentPtr: not 0 } e || !EntityReader.Read(h, e.ParentPtr, buf)) { return null; }

            var index = Children(h, buf).IndexOf(cursor);
            if (index < 0) { return null; }

            path.Insert(0, index);
            cursor = e.ParentPtr;
        }

        return cursor == root ? path.ToArray() : null;
    }

    /// <summary>Kokten yolu izleyip ogeyi bulur; yol tutmuyorsa 0.</summary>
    internal static ulong Resolve(IntPtr h, ulong root, IReadOnlyList<int> path)
    {
        var cursor = root;
        var buf = new byte[TextOffset + 0x20];
        foreach (var index in path)
        {
            if (!EntityReader.Read(h, cursor, buf) || BitConverter.ToUInt64(buf, OffSelf) != cursor) { return 0; }

            var kids = Children(h, buf);
            if (index >= kids.Count) { return 0; }

            cursor = kids[index];
        }

        return EntityReader.Read(h, cursor, buf) && BitConverter.ToUInt64(buf, OffSelf) == cursor ? cursor : 0;
    }

    internal static ulong Ancestor(IntPtr h, ulong element, int levels)
    {
        var cursor = element;
        for (var i = 0; i < levels; i++)
        {
            if (UiFinder.Reread(h, cursor) is not { ParentPtr: not 0 } e) { return 0; }
            cursor = e.ParentPtr;
        }

        return cursor;
    }

    internal static List<ulong> Children(IntPtr h, byte[] element)
    {
        var list = new List<ulong>();
        var first = BitConverter.ToUInt64(element, OffChildren);
        var last = BitConverter.ToUInt64(element, OffChildren + 8);
        if (first == 0 || last <= first || last - first > 8UL * MaxChildren) { return list; }

        var raw = new byte[(int)(last - first)];
        if (!EntityReader.Read(h, first, raw)) { return list; }

        for (var i = 0; i + 8 <= raw.Length; i += 8)
        {
            var child = BitConverter.ToUInt64(raw, i);
            if (PlayerChain.LooksLikePointer(child)) { list.Add(child); }
        }

        return list;
    }
}
