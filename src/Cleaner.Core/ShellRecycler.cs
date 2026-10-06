using System.Runtime.InteropServices;

namespace Cleaner.Core;

public sealed class ShellRecycler : IRecycler
{
    public void Recycle(string path, Action validateAgain)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Windows recycling requires an STA thread.");
        IShellItem? item = null;
        IFileOperation? operation = null;
        try
        {
            var id = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out item));
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), throwOnError: true)!)!;
            // Recycle only, add Explorer undo, no connected companion files, no elevation.
            // Abort on errors. The progress sink vetoes permanent-delete fallback.
            operation.SetOperationFlags(0x00080000 | 0x20000000 | 0x00002000 | 0x00000400 | 0x00100000 | 0x00000004 | 0x00000010);
            var sink = new RecycleSink(validateAgain);
            operation.DeleteItem(item, sink);
            try { operation.PerformOperations(); }
            catch (COMException ex) when (sink.Failure is not null)
            {
                if (sink.Cancelled) throw new OperationCanceledException(sink.Failure, ex);
                throw new IOException(sink.Failure, ex);
            }
            operation.GetAnyOperationsAborted(out bool aborted);
            if (sink.Failure is not null) throw new IOException(sink.Failure);
            if (aborted || !sink.Succeeded || File.Exists(path))
                throw new IOException("Windows did not confirm recycling this file. It was skipped.");
        }
        finally
        {
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
            if (item is not null) Marshal.FinalReleaseComObject(item);
        }
    }

    // This method is also exercised directly by tests with the permanent-delete flag absent.
    public static bool IsRecycleTransfer(uint flags) => (flags & 0x80) != 0;

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleSink(Action validate) : IFileOperationProgressSink
    {
        public bool Succeeded { get; private set; }
        public string? Failure { get; private set; }
        public bool Cancelled { get; private set; }
        public int PreDeleteItem(uint flags, IntPtr item)
        {
            try
            {
                if (!IsRecycleTransfer(flags)) throw new IOException("This file cannot be recycled. Permanent deletion is blocked.");
                validate();
                return 0;
            }
            catch (Exception ex) { Failure = ex.Message; Cancelled = ex is OperationCanceledException; return unchecked((int)0x80004004); }
        }
        public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem)
        {
            Succeeded = result >= 0 && IsRecycleTransfer(flags) && newItem != IntPtr.Zero;
            if (!Succeeded) Failure ??= $"Windows recycling failed (0x{result:X8}).";
            return 0;
        }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint f, IntPtr i, string n) => 0;
        public int PostRenameItem(uint f, IntPtr i, string n, int r, IntPtr ni) => 0;
        public int PreMoveItem(uint f, IntPtr i, IntPtr d, string n) => 0;
        public int PostMoveItem(uint f, IntPtr i, IntPtr d, string n, int r, IntPtr ni) => 0;
        public int PreCopyItem(uint f, IntPtr i, IntPtr d, string n) => 0;
        public int PostCopyItem(uint f, IntPtr i, IntPtr d, string n, int r, IntPtr ni) => 0;
        public int PreNewItem(uint f, IntPtr d, string n) => 0;
        public int PostNewItem(uint f, IntPtr d, string n, string t, uint a, int r, IntPtr ni) => 0;
        public int UpdateProgress(uint total, uint soFar) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid id,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bind, ref Guid handler, ref Guid id, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint name, out IntPtr result);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hints, out int order);
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(uint window);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
        void MoveItems(IntPtr items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
        void CopyItems(IntPtr items, IShellItem destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        void DeleteItems(IntPtr items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink? sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint f, IntPtr i, [MarshalAs(UnmanagedType.LPWStr)] string n);
        [PreserveSig] int PostRenameItem(uint f, IntPtr i, [MarshalAs(UnmanagedType.LPWStr)] string n, int r, IntPtr ni);
        [PreserveSig] int PreMoveItem(uint f, IntPtr i, IntPtr d, [MarshalAs(UnmanagedType.LPWStr)] string n);
        [PreserveSig] int PostMoveItem(uint f, IntPtr i, IntPtr d, [MarshalAs(UnmanagedType.LPWStr)] string n, int r, IntPtr ni);
        [PreserveSig] int PreCopyItem(uint f, IntPtr i, IntPtr d, [MarshalAs(UnmanagedType.LPWStr)] string n);
        [PreserveSig] int PostCopyItem(uint f, IntPtr i, IntPtr d, [MarshalAs(UnmanagedType.LPWStr)] string n, int r, IntPtr ni);
        [PreserveSig] int PreDeleteItem(uint f, IntPtr i);
        [PreserveSig] int PostDeleteItem(uint f, IntPtr i, int r, IntPtr ni);
        [PreserveSig] int PreNewItem(uint f, IntPtr d, [MarshalAs(UnmanagedType.LPWStr)] string n);
        [PreserveSig] int PostNewItem(uint f, IntPtr d, [MarshalAs(UnmanagedType.LPWStr)] string n,
            [MarshalAs(UnmanagedType.LPWStr)] string t, uint a, int r, IntPtr ni);
        [PreserveSig] int UpdateProgress(uint total, uint soFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
}
