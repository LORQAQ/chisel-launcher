using System.Runtime.InteropServices;

namespace Chisel.Core.Util;

public static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    public static long TotalPhysicalMb()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)(status.ullTotalPhys / 1024 / 1024) : 8192;
    }

    public static long AvailablePhysicalMb()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)(status.ullAvailPhys / 1024 / 1024) : 4096;
    }

    public static int RecommendedGameMemoryMb()
    {
        var total = TotalPhysicalMb();
        return total switch
        {
            < 7000 => 2048,
            < 11000 => 3072,
            < 20000 => 4096,
            < 30000 => 6144,
            _ => 8192
        };
    }

    public static int DefaultGameMemoryMb()
    {
        var recommended = RecommendedGameMemoryMb();
        var available = AvailablePhysicalMb();
        return (int)Math.Max(1024, Math.Min(recommended, available - 2048));
    }
}
