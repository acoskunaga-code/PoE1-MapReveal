using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Poe2Map;

/// <summary>
///     Bir .NET derlemesinin (ornegin GameOffsets.dll) yapilarini ve alan offsetlerini METADATA'dan okur.
///     Derleme calistirilmiyor ve hatta yuklenmiyor: dosya PEReader ile bayt olarak okunuyor, FieldLayout
///     tablosundaki acik offsetler ([FieldOffset]) yazdiriliyor. Ucuncu taraf ikili dosyayi guvenle incelemenin yolu.
///
///     Kullanim:
///         dllfields <dll>            tur adlari (kompakt liste)
///         dllfields <dll> <regex>    adi eslesen turlerin alanlari, ofsete gore
///
///     Buradan cikan her sayi IPUCUDUR; programa girmeden once kendi canli testimizle dogrulanir.
/// </summary>
internal static class DllFieldsCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("kullanim: dllfields <dll> [regex]");
            return 2;
        }

        using var stream = File.OpenRead(args[0]);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) { Console.WriteLine("metadata yok (yonetilen derleme degil)"); return 1; }

        var md = pe.GetMetadataReader();
        var filter = args.Length > 1 ? new Regex(args[1], RegexOptions.IgnoreCase) : null;
        var provider = new TypeNames();

        var names = new List<string>();
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            var name = FullName(md, type);
            if (name == "<Module>") { continue; }

            if (filter == null)
            {
                names.Add(name + (type.GetFields().Count > 0 ? $"({type.GetFields().Count})" : ""));
                continue;
            }

            if (!filter.IsMatch(name)) { continue; }

            var layout = type.GetLayout();
            var kind = (type.Attributes & System.Reflection.TypeAttributes.LayoutMask) switch
            {
                System.Reflection.TypeAttributes.ExplicitLayout => "ACIK",
                System.Reflection.TypeAttributes.SequentialLayout => "sirali",
                _ => "otomatik",
            };
            Console.WriteLine($"\n{name}   duzen {kind}, boyut {(layout.IsDefault ? "-" : "0x" + layout.Size.ToString("X"))}, " +
                              $"paketleme {(layout.IsDefault ? "-" : layout.PackingSize.ToString())}");
            var rows = new List<(int Offset, string Line)>();
            foreach (var fh in type.GetFields())
            {
                var field = md.GetFieldDefinition(fh);
                if ((field.Attributes & System.Reflection.FieldAttributes.Static) != 0)
                {
                    // Sabit ya da statik alanlar: offsetler burada tutuluyor olabilir.
                    var constant = field.GetDefaultValue().IsNil ? "" : " = " + ConstantText(md, field.GetDefaultValue());
                    rows.Add((int.MaxValue, $"  statik    {md.GetString(field.Name)}{constant}"));
                    continue;
                }

                var offset = field.GetOffset();
                var fieldType = field.DecodeSignature(provider, null);
                var fieldName = md.GetString(field.Name);

                // Offsetler [FieldOffset] yerine ozel bir oznitelikte tutuluyor olabilir.
                var attrs = new List<string>();
                foreach (var ah in field.GetCustomAttributes())
                {
                    var attr = md.GetCustomAttribute(ah);
                    var blob = md.GetBlobReader(attr.Value);
                    blob.ReadUInt16();
                    var arg = blob.RemainingBytes >= 4 ? "0x" + blob.ReadInt32().ToString("X") : "";
                    attrs.Add($"{AttributeName(md, attr.Constructor)}({arg})");
                }

                var attrText = attrs.Count > 0 ? "   [" + string.Join(", ", attrs) + "]" : "";
                rows.Add((offset, $"  {(offset >= 0 ? "+0x" + offset.ToString("X") : "(sirali)"),-9} {fieldName} : {fieldType}{attrText}"));
            }

            foreach (var row in rows.OrderBy(r => r.Offset)) { Console.WriteLine(row.Line); }
        }

        if (filter == null)
        {
            Console.WriteLine($"{names.Count} tur:");
            for (var i = 0; i < names.Count; i += 4)
            {
                Console.WriteLine("  " + string.Join(", ", names.Skip(i).Take(4)));
            }
        }

        return 0;
    }

    private static string AttributeName(MetadataReader md, EntityHandle ctor)
    {
        if (ctor.Kind == HandleKind.MemberReference)
        {
            var member = md.GetMemberReference((MemberReferenceHandle)ctor);
            return member.Parent.Kind == HandleKind.TypeReference
                ? md.GetString(md.GetTypeReference((TypeReferenceHandle)member.Parent).Name)
                : "?";
        }

        if (ctor.Kind == HandleKind.MethodDefinition)
        {
            var method = md.GetMethodDefinition((MethodDefinitionHandle)ctor);
            return md.GetString(md.GetTypeDefinition(method.GetDeclaringType()).Name);
        }

        return "?";
    }

    private static string ConstantText(MetadataReader md, ConstantHandle handle)
    {
        var constant = md.GetConstant(handle);
        var reader = md.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Int32 => "0x" + reader.ReadInt32().ToString("X"),
            ConstantTypeCode.UInt32 => "0x" + reader.ReadUInt32().ToString("X"),
            ConstantTypeCode.Int64 => "0x" + reader.ReadInt64().ToString("X"),
            ConstantTypeCode.UInt64 => "0x" + reader.ReadUInt64().ToString("X"),
            ConstantTypeCode.Int16 => "0x" + reader.ReadInt16().ToString("X"),
            ConstantTypeCode.UInt16 => "0x" + reader.ReadUInt16().ToString("X"),
            ConstantTypeCode.Byte => "0x" + reader.ReadByte().ToString("X"),
            _ => constant.TypeCode.ToString(),
        };
    }

    private static string FullName(MetadataReader md, TypeDefinition type)
    {
        var name = md.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) { return FullName(md, md.GetTypeDefinition(declaring)) + "+" + name; }

        var ns = md.GetString(type.Namespace);
        return ns.Length > 0 ? ns + "." + name : name;
    }

    /// <summary>Alan tiplerini okunur ada cevirir; sadece yazdirma icin.</summary>
    private sealed class TypeNames : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[,]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(",", typeArguments) + ">";

        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? context, int index) => "!" + index;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;
    }
}
