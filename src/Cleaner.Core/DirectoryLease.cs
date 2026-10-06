using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cleaner.Core;

// Prevent ancestor renames while a Shell operation is running. OPEN_REPARSE_POINT
// lets us reject redirected directories from their handles, without following them.
internal sealed class DirectoryLease : IDisposable
{
    private readonly List<SafeFileHandle> handles = [];
    public static DirectoryLease Acquire(string file)
    {
        var lease = new DirectoryLease();
        try
        {
            var chain = new Stack<string>();
            for (string? folder = Path.GetDirectoryName(file); folder is not null; folder = Path.GetDirectoryName(folder))
                chain.Push(folder);
            foreach (var folder in chain)
            {
                var handle = CreateFile(folder, 0x80000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message); }
                lease.handles.Add(handle);
                if (!GetFileInformationByHandle(handle, out var info) ||
                    (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                    (info.Attributes & (uint)FileAttributes.Directory) == 0)
                    throw new IOException("An ancestor is linked or cannot be verified.");
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    public void Dispose() { foreach (var handle in handles) handle.Dispose(); handles.Clear(); }
    [StructLayout(LayoutKind.Sequential)]
    private struct Information
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, LastAccess, LastWrite;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information info);
}
