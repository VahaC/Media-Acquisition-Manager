using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MediaAcquisitionManager.Infrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

/// <summary>
/// MAM-26 — what the quarantine's Linux tests make and read beside the code under test: a named pipe, and the mount a
/// path is on, read the way the quarantine reads it (<see cref="LinuxCalls"/>), so a test can tell two mounts apart or
/// write a mount table line for the mount it runs on. MAM-39 — and a folder's owner and the process's effective ids,
/// read another way than the quarantine reads them.
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

    /// <summary>
    /// MAM-39 — the owner and group of <paramref name="path"/>, as coreutils' <c>stat</c> reads them: not through
    /// <see cref="LinuxCalls"/> or <see cref="StatxBuffer"/>, so a field read at the wrong offset there can't pass for
    /// the truth here.
    /// </summary>
    public static (uint UserId, uint GroupId) OwnerOf(string path)
    {
        var startInfo = new ProcessStartInfo("stat")
        {
            ArgumentList = { "-c", "%u %g", "--", path },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"stat failed with exit code {process.ExitCode}: {errors.Result}");

        var ids = output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (uint.Parse(ids[0], CultureInfo.InvariantCulture), uint.Parse(ids[1], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// MAM-39 — the test process's effective user and group ids, from the second column of <c>Uid:</c> and <c>Gid:</c>
    /// in <c>/proc/self/status</c> (real, effective, saved, file system): read beside the code under test, not through
    /// its <c>geteuid</c> and <c>getegid</c>.
    /// </summary>
    public static (uint UserId, uint GroupId) EffectiveIds()
    {
        var lines = File.ReadAllLines("/proc/self/status");
        return (EffectiveOf(lines, "Uid:"), EffectiveOf(lines, "Gid:"));

        static uint EffectiveOf(string[] lines, string key) =>
            uint.Parse(
                lines.Single(line => line.StartsWith(key, StringComparison.Ordinal))
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[2],
                CultureInfo.InvariantCulture);
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
