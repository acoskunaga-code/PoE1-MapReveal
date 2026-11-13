using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Poe2Map;

/// <summary>
///     ExileApi'nin harita simgesi listesi (ExileCore.dll icindeki MapIconsIndex enum'u) -> "indeks TAB ad" satirlari.
///     Derleme calistirilmiyor, hatta yuklenmiyor: sabit alanlar metadata'dan okunuyor. MapScan iconcatalog ve overlay'in
///     ilk acilis indirmesi (MapOverlay\IconDownload.cs) ayni kodu kullaniyor.
/// </summary>
internal static class IconCatalogFile
{
    internal const string EnumName = "ExileCore.Shared.Enums.MapIconsIndex";

    /// <summary>Simgeler indekse gore sirali; indeks 0 ("None") atlaniyor, atlasta hucresi yok. Yonetilen derleme degilse bos.</summary>
    internal static List<(int Index, string Name)> ReadNames(Stream dll)
    {
        using var pe = new PEReader(dll, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata) { return new List<(int, string)>(); }

        var md = pe.GetMetadataReader();
        var rows = new List<(int Index, string Name)>();
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            if ($"{md.GetString(type.Namespace)}.{md.GetString(type.Name)}" != EnumName) { continue; }

            foreach (var fh in type.GetFields())
            {
                var field = md.GetFieldDefinition(fh);
                if ((field.Attributes & FieldAttributes.Static) == 0 || field.GetDefaultValue().IsNil) { continue; }

                var constant = md.GetConstant(field.GetDefaultValue());
                if (constant.TypeCode != ConstantTypeCode.Int32) { continue; }

                rows.Add((md.GetBlobReader(constant.Value).ReadInt32(), md.GetString(field.Name)));
            }
        }

        return rows.Where(r => r.Index > 0).OrderBy(r => r.Index).ToList();
    }

    /// <summary>Once .part'a yaziyor, sonra yerine tasiyor: yarim kalan bir yazma eksik katalog birakmasin.</summary>
    internal static void Write(IReadOnlyCollection<(int Index, string Name)> rows, string output, string source = EnumName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var lines = new List<string> { $"# {source}, {rows.Count} simge (indeks, ad)" };
        lines.AddRange(rows.Select(r => $"{r.Index}\t{r.Name}"));
        File.WriteAllLines(output + ".part", lines);
        File.Move(output + ".part", output, overwrite: true);
    }
}
