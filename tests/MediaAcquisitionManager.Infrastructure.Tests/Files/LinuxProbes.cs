using System.ComponentModel;
using System.Runtime.InteropServices;
using MediaAcquisitionManager.Infrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

/// <summary>
/// MAM-26 — what the quarantine's Linux tests make and read beside the code under test: a named pipe, and the mount a
/// path is on, read the way the quarantine reads it (<see cref="LinuxCalls"/>), so a test can tell two mounts apart or
/// write a mount table line for the mount it runs on.
/// </summary>
internal static class LinuxProbes
{
    /// <summary>A named pipe at <paramref name="path"/>, mode 0644.</summary>
    public static void CreateNamedPipe(string path)
    {
        if (MakeFifo(path, Convert.ToUInt32("644", 8)) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>The mount <paramref name="path"/> is on (<c>stx_mnt_id</c>), following links; <c>null</c> when the
    /// kernel doesn't say (before Linux 5.8).</summary>
    public static ulong? MountIdOf(string path)
    {
        if (LinuxCalls.Statx(LinuxCalls.CurrentFolderDescriptor, path, 0, LinuxCalls.MountIdReported, out var status) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        return (status.Mask & LinuxCalls.MountIdReported) != 0 ? status.MountId : null;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
