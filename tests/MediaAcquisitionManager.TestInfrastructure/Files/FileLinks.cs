using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// Makes the links the file inventory tests need (MAM-9), on Linux and on a Windows box without
/// administrator rights or Developer Mode.
/// </summary>
/// <remarks>
/// Windows lets only an administrator, or anyone with Developer Mode on, create a symbolic link; a junction
/// (a link to a folder) and a hard link need no privilege. So a link to a folder is a junction there, and a
/// symbolic link is only tried, for the test to skip when it is refused.
/// </remarks>
public static class FileLinks
{
    /// <summary>The HRESULT of <c>ERROR_PRIVILEGE_NOT_HELD</c>: creating a symbolic link without the right.</summary>
    private const int PrivilegeNotHeld = unchecked((int)0x80070522);

    /// <summary>Why a symbolic-link test skips where links can't be made.</summary>
    public const string SymbolicLinksUnavailable =
        "This Windows account may not create symbolic links (turn on Developer Mode, or run as an administrator); " +
        "the test runs on Linux in CI.";

    /// <summary>
    /// A link at <paramref name="link"/> to the folder <paramref name="target"/>: a symbolic link, or on Windows a
    /// junction, which must point at a full path.
    /// </summary>
    public static void CreateFolderLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        if (!Path.IsPathFullyQualified(target))
            throw new ArgumentException("A junction needs a full path as its target.", nameof(target));

        // mklink is built into cmd; quoted for the paths, which come from the temp folder and have no quotes.
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mklink /J failed with exit code {process.ExitCode}: {errors.Result}{output}");
    }

    /// <summary>
    /// A symbolic link at <paramref name="link"/> to <paramref name="target"/>, relative or full, to a file or a
    /// folder: <c>false</c> when this Windows account may not create one (<see cref="SymbolicLinksUnavailable"/>).
    /// </summary>
    public static bool TryCreateSymbolicLink(string link, string target, bool toFolder = false)
    {
        try
        {
            if (toFolder)
                Directory.CreateSymbolicLink(link, target);
            else
                File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException exception) when (OperatingSystem.IsWindows() && exception.HResult == PrivilegeNotHeld)
        {
            return false;
        }
    }

    /// <summary>A second name, <paramref name="newPath"/>, for the file <paramref name="existingPath"/>.</summary>
    public static void CreateHardLink(string existingPath, string newPath)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkW(newPath, existingPath, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            if (LinkPosix(existingPath, newPath) != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return;
        }

        throw new PlatformNotSupportedException("Hard links are made on Linux and Windows only.");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("libc.so.6", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkPosix(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);
}
