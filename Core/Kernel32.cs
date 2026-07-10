using System.Runtime.InteropServices;

namespace CS2Cheat.Core;

public abstract class Kernel32
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        [Out] IntPtr lpBuffer, int dwSize, out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    public static bool WriteMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer)
    {
        return WriteProcessMemory(hProcess, lpBaseAddress, lpBuffer, lpBuffer.Length, out _);
    }
}