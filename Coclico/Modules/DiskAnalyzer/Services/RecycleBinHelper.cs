using System.Runtime.InteropServices;
using Coclico.Services;

namespace Coclico.Modules.DiskAnalyzer.Services;

public static class RecycleBinHelper
{
    private const uint FO_DELETE = 3;
    private const uint FOF_ALLOWUNDO = 0x40;
    private const uint FOF_NOCONFIRMATION = 0x10;
    private const uint FOF_NOERRORUI = 0x400;
    private const uint FOF_SILENT = 0x4;
    private const uint FOFX_RECYCLEONDELETE = 0x80;

    public static bool SendToRecycleBin(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return true;
        }

        try
        {
            return SendToRecycleBinIFileOperation(paths);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "RecycleBinHelper.IFileOperation");
            return SendToRecycleBinLegacy(paths);
        }
    }

    private static bool SendToRecycleBinIFileOperation(IReadOnlyList<string> paths)
    {
        Type? fileType = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"));
        object fileOperation = fileType is null
            ? throw new InvalidOperationException("IFileOperation COM type unavailable")
            : Activator.CreateInstance(fileType)!;

        try
        {
            var op = (IFileOperation)fileOperation;
            op.SetOperationFlags(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOFX_RECYCLEONDELETE);

            foreach (string path in paths)
            {
                SHCreateItemFromParsingName(path, nint.Zero, typeof(IShellItem).GUID, out object item);
                op.DeleteItem((IShellItem)item, nint.Zero);
            }

            int hr = op.PerformOperations();
            return hr == 0;
        }
        finally
        {
            _ = Marshal.ReleaseComObject(fileOperation);
        }
    }

    private static bool SendToRecycleBinLegacy(IReadOnlyList<string> paths)
    {
        var operation = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", paths) + "\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT)
        };

        int result = SHFileOperation(ref operation);
        return result == 0 && !operation.fAnyOperationsAborted;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCTW
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCTW operation);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out object ppv);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("947aab5f-0a5e-4c25-9743-5c861dae6034")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig]
        int Advise(nint pfops);
        [PreserveSig]
        int Unadvise(uint dwCookie);
        [PreserveSig]
        int SetOperationFlags(uint dwOperationFlags);
        [PreserveSig]
        int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        [PreserveSig]
        int SetProgressDialog(nint popd);
        [PreserveSig]
        int SetProperties(nint pproparray);
        [PreserveSig]
        int SetOwnerWindow(uint hwndOwner);
        [PreserveSig]
        int ApplyPropertiesToItem(IShellItem psiItem);
        [PreserveSig]
        int ApplyPropertiesToItems(nint punkItems);
        [PreserveSig]
        int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, nint pfopsItem);
        [PreserveSig]
        int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, nint pfopsItem);
        [PreserveSig]
        int MoveItems(nint punkItems, IShellItem psiDestinationFolder);
        [PreserveSig]
        int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, nint pfopsItem);
        [PreserveSig]
        int CopyItems(nint punkItems, IShellItem psiDestinationFolder);
        [PreserveSig]
        int DeleteItem(IShellItem psiItem, nint pfopsItem);
        [PreserveSig]
        int DeleteItems(nint punkItems);
        [PreserveSig]
        int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, nint pfopsItem);
        [PreserveSig]
        int PerformOperations();
        [PreserveSig]
        int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }
}
