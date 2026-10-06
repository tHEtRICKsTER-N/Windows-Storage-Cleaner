using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cleaner.Core;

internal static class FileIdentity
{
    public static FileSnapshot Read(string path)
    {
        using var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        if ((info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.System |
            FileAttributes.ReadOnly | FileAttributes.Offline | FileAttributes.Encrypted)) != 0 || info.NumberOfLinks != 1)
            throw new IOException("Linked or protected file is excluded.");
        return new(path, ((long)info.SizeHigh << 32) | info.SizeLow,
            ToDate(info.LastWrite), ToDate(info.Creation),
            $"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}");
    }
    private static DateTime ToDate(System.Runtime.InteropServices.ComTypes.FILETIME time) =>
        DateTime.FromFileTimeUtc(((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct Information
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, LastAccess, LastWrite;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information info);
}
