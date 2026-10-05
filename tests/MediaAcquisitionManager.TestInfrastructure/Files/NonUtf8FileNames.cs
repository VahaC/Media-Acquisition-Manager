using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// Files and folders whose names aren't valid UTF-8, on Linux (MAM-18), removed again when this is disposed.
/// </summary>
/// <remarks>
/// .NET writes every name as UTF-8, so such a name is made through libc from its bytes. .NET also reads one back
/// with U+FFFD in place of what isn't UTF-8, so it can't name it to delete it, and <see cref="TemporaryFolder"/>
/// would fail on the folder holding it: dispose this first, which removes what it made, last made first.
/// </remarks>
public sealed class NonUtf8FileNames : IDisposable
{
    /// <summary>A byte no UTF-8 text has.</summary>
    public const byte InvalidByte = 0xFF;

    private const uint FolderMode = 0x1ED;   // 0755
    private const uint FileMode = 0x1A4;     // 0644

    private readonly Stack<(byte[] Path, bool IsFolder)> _made = new();

    /// <summary>The bytes of the path of <paramref name="name"/> inside <paramref name="folder"/>.</summary>
    public static byte[] PathOf(string folder, ReadOnlySpan<byte> name) => [.. Encoding.UTF8.GetBytes(folder), (byte)'/', .. name];

    /// <summary>The bytes of the path of <paramref name="name"/> inside the folder whose path is the bytes
    /// <paramref name="folder"/>.</summary>
    public static byte[] PathOf(byte[] folder, string name) => [.. folder, (byte)'/', .. Encoding.UTF8.GetBytes(name)];

    /// <summary>Makes the folder at the bytes <paramref name="path"/>; its parent must exist.</summary>
    public void CreateFolder(byte[] path)
    {
        if (MakeFolder(Terminated(path), FolderMode) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        _made.Push((path, true));
    }

    /// <summary>Makes an empty file at the bytes <paramref name="path"/>; its folder must exist.</summary>
    public void CreateFile(byte[] path)
    {
        var descriptor = Create(Terminated(path), FileMode);
        if (descriptor < 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        Close(descriptor);
        _made.Push((path, false));
    }

    /// <summary>Removes what was made, last made first; what is gone already is passed over.</summary>
    public void Dispose()
    {
        while (_made.TryPop(out var made))
        {
            if (made.IsFolder)
                RemoveFolder(Terminated(made.Path));
            else
                Unlink(Terminated(made.Path));
        }
    }

    /// <summary>The C string libc reads: the bytes and a NUL.</summary>
    private static byte[] Terminated(byte[] path) => [.. path, 0];

    [DllImport("libc.so.6", EntryPoint = "mkdir", SetLastError = true)]
    private static extern int MakeFolder(byte[] path, uint mode);

    [DllImport("libc.so.6", EntryPoint = "creat", SetLastError = true)]
    private static extern int Create(byte[] path, uint mode);

    [DllImport("libc.so.6", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);

    [DllImport("libc.so.6", EntryPoint = "unlink", SetLastError = true)]
    private static extern int Unlink(byte[] path);

    [DllImport("libc.so.6", EntryPoint = "rmdir", SetLastError = true)]
    private static extern int RemoveFolder(byte[] path);
}
