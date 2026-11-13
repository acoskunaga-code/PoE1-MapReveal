using System.Text;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Canli alanin KIMLIGI (Radar'in alan kimligi: "2_8_12_1"). Alan nesnesinden okunan bir metinden cikiyor:
///     alanin yer grafigi yolu "Metadata/Terrain/Act8/Area12Level1/Graphs/temple" (canlida alan+0x8F0 -> +0x20 -> metin).
///     Offset sabit degil: alan nesnesinin ilk 0x1200 baytindaki isaretcilerin gosterdigi yerde (ve bir hop sonrasinda)
///     bu kaliba uyan ilk metin aranir; yama offseti kaydirsa da bulunur.
///
///     Kimlik: bolum = (act &lt;= 5 ? 1 : 2), sonra act, alan numarasi (+ varsa harf), varsa Level numarasi:
///         Act8/Area12Level1 -> 2_8_12_1,   Act3/Area8Level2 -> 1_3_8_2,   Act8/Area9 -> 2_8_9
///     Kampanya disi alanlarda (haritalar, ligler) kalip yok: null.
/// </summary>
internal static class AreaIdentity
{
    private const int AreaWindow = 0x1200;

    private static readonly Regex Campaign = new(@"Metadata/Terrain/Act(\d+)/Area(\d+)([a-z]?)(?:Level(\d+))?", RegexOptions.Compiled);

    /// <summary>Alanin Radar kimligi; kampanya alani degilse ya da okunamazsa null.</summary>
    internal static string? RadarId(IntPtr h, ulong area)
    {
        var path = TerrainPath(h, area);
        if (path is null) { return null; }

        var m = Campaign.Match(path);
        if (!m.Success) { return null; }

        var act = int.Parse(m.Groups[1].Value);
        var id = $"{(act <= 5 ? 1 : 2)}_{act}_{int.Parse(m.Groups[2].Value)}{m.Groups[3].Value}";
        return m.Groups[4].Success ? $"{id}_{int.Parse(m.Groups[4].Value)}" : id;
    }

    /// <summary>Alan nesnesinden ulasilan, kampanya yer grafigi yoluna benzeyen ilk metin.</summary>
    internal static string? TerrainPath(IntPtr h, ulong area)
    {
        var window = new byte[AreaWindow];
        if (!EntityReader.Read(h, area, window)) { return null; }

        var record = new byte[0x80];
        for (var o = 0; o + 8 <= window.Length; o += 8)
        {
            var p = BitConverter.ToUInt64(window, o);
            if (!PlayerChain.LooksLikePointer(p)) { continue; }

            if (Matches(TextAt(h, p)) is { } direct) { return direct; }
            if (!EntityReader.Read(h, p, record)) { continue; }

            for (var q = 0; q + 8 <= record.Length; q += 8)
            {
                var pp = BitConverter.ToUInt64(record, q);
                if (PlayerChain.LooksLikePointer(pp) && Matches(TextAt(h, pp)) is { } deep) { return deep; }
            }
        }

        return null;
    }

    private static string? Matches(string text) =>
        text.StartsWith("Metadata/Terrain/Act", StringComparison.Ordinal) && Campaign.IsMatch(text) ? text : null;

    /// <summary>Adreste UTF-16 ya da ASCII bir metin (sonlandirici null'a kadar, yazdirilabilir).</summary>
    private static string TextAt(IntPtr h, ulong address)
    {
        var buf = new byte[0x60];
        if (!EntityReader.Read(h, address, buf)) { return ""; }

        var wide = new StringBuilder();
        for (var i = 0; i + 1 < buf.Length; i += 2)
        {
            var c = buf[i] | (buf[i + 1] << 8);
            if (c == 0) { break; }
            if (c < 0x20 || c > 0x7E) { wide.Clear(); break; }
            wide.Append((char)c);
        }

        if (wide.Length >= 3) { return wide.ToString(); }

        var narrow = new StringBuilder();
        foreach (var b in buf)
        {
            if (b == 0) { break; }
            if (b < 0x20 || b > 0x7E) { narrow.Clear(); break; }
            narrow.Append((char)b);
        }

        return narrow.ToString();
    }
}
