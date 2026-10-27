using System.Runtime.InteropServices;

namespace Coclico.Modules.UpdateCenter.Services;

public class RestorePointService
{
    public const int DriverUpdateType = 12;

    public virtual bool Create(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > 64)
            throw new ArgumentException("Description must be between 1 and 64 characters.");

        var state = new RestoreState
        {
            dwEventType = 100,
            dwRestorePointType = DriverUpdateType,
            llSequenceNumber = 0,

            szDescription = description
        };
        return SRSetRestorePointW(ref state, out _);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RestoreState
    {
        public int dwEventType;
        public int dwRestorePointType;
        public long llSequenceNumber;
        public string szDescription;
    }

    [DllImport("SrClient.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SRSetRestorePointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(ref RestoreState restorePoint, out long sequenceNumber);
}
