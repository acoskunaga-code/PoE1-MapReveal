using System.Runtime.InteropServices;

namespace Poe2Map;

/// <summary>Oyun surecinin taranabilir bellek bolgeleri; tarayicilarin (zemin, UI ogeleri, sinif goruntusu) ortak girdisi.</summary>
internal static class MemoryRegions
{
    /// <summary>Yazilabilir ozel bellek bolgeleri (heap). TerrainFinder ile ayni olcut.</summary>
    internal static List<(ulong Base, long Size)> Heap(IntPtr handle)
    {
        var mbiSize = (IntPtr)Marshal.SizeOf<Native.MemoryBasicInformation>();
        var regions = new List<(ulong Base, long Size)>();
        ulong address = 0x10000;

        while (address < 0x7FFFFFFF0000UL)
        {
            if (Native.VirtualQueryEx(handle, (IntPtr)address, out var mbi, mbiSize) == IntPtr.Zero) { break; }
            var size = (long)mbi.RegionSize;
            if (size <= 0) { break; }

            if (mbi.State == Native.MemCommit &&
                mbi.Type == Native.MemPrivate &&
                (mbi.Protect & Native.PageGuard) == 0 &&
                mbi.Protect == Native.PageReadWrite)
            {
                regions.Add(((ulong)mbi.BaseAddress.ToInt64(), size));
            }

            address += (ulong)size;
        }

        return regions;
    }
}
