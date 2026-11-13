using System.Collections.Concurrent;

namespace Poe2Map;

/// <summary>
///     Bellekteki butun UI ogelerinin HAM goruntusu. Oge = +0x08'i kendi adresini gosteren yapi
///     (UiFinder.Find'in kullandigi sinav). Orada alanlar PoE2 offsetleriyle okunuyordu; burada
///     ilk Window bayt oldugu gibi saklaniyor ki yerlesim oyun surumune bakmadan ogrenilebilsin.
/// </summary>
internal sealed class UiSnapshot
{
    internal const int Window = 0x480;
    private const int ReadBlock = 1 << 20;

    internal readonly ConcurrentDictionary<ulong, byte[]> Elements = new();

    /// <summary>selfOffset: "kendi adresini gosteren alan"in nesne basindan uzakligi (PoE2'de 8; PoE1'de selfscan'le bulunur).</summary>
    internal static UiSnapshot Take(IntPtr handle, int selfOffset = 8)
    {
        var snapshot = new UiSnapshot();

        Parallel.ForEach(
            TerrainLayoutProbe.Regions(handle),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            () => new byte[ReadBlock],
            (region, _, buf) =>
            {
                for (long off = 0; off < region.Size; off += ReadBlock - Window)
                {
                    var want = (int)Math.Min(ReadBlock, region.Size - off);
                    if (want < Window + 16) { break; }
                    if (!Native.ReadProcessMemory(handle, (IntPtr)(region.Base + (ulong)off), buf,
                            (IntPtr)want, out var got) || (long)got < Window + 16)
                    {
                        continue;
                    }

                    var n = (int)(long)got;
                    for (var i = 0; i + Window <= n; i += 8)
                    {
                        var here = region.Base + (ulong)off + (ulong)i;
                        if (BitConverter.ToUInt64(buf, i + selfOffset) != here) { continue; }

                        var raw = new byte[Window];
                        Buffer.BlockCopy(buf, i, raw, 0, Window);
                        snapshot.Elements[here] = raw;
                    }
                }

                return buf;
            },
            _ => { });

        return snapshot;
    }

    /// <summary>Diske yazar: analiz sonradan, kullanicidan yeni bir olcum istemeden tekrar yapilabilsin.</summary>
    internal void Save(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(this.Elements.Count);
        foreach (var (address, raw) in this.Elements)
        {
            writer.Write(address);
            writer.Write(raw);
        }
    }

    internal static UiSnapshot Load(string path)
    {
        var loaded = new UiSnapshot();
        using var reader = new BinaryReader(File.OpenRead(path));
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var address = reader.ReadUInt64();
            loaded.Elements[address] = reader.ReadBytes(Window);
        }

        return loaded;
    }
}
