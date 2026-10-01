using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// Opens one file the way the platform allows and reads what it says about it (MAM-9). A Strategy, one per
/// platform, because the file's identity comes from a different system call on each — <c>statx</c> on Linux,
/// the file handle's information on Windows, nothing elsewhere — and so does keeping the file that was checked
/// the one ffprobe reads (ADR 0009).
/// </summary>
/// <remarks>
/// Like <see cref="IFileInventory"/>, it returns its expected failures and never throws them.
/// </remarks>
internal interface IFileStatusReader
{
    /// <summary>Whether the facts it gives carry the file's device and inode.</summary>
    bool ReportsFileIdentity { get; }

    /// <summary>
    /// Opens the regular file at <paramref name="systemPath"/> for reading, which proves the app may read it:
    /// <see cref="FileInventoryStatus.Found"/> and the open file, which the caller disposes, or
    /// <see cref="FileInventoryStatus.NotFound"/>, <see cref="FileInventoryStatus.NotARegularFile"/> or
    /// <see cref="FileInventoryStatus.Unreadable"/>. The caller hands it a path with no link left
    /// (<see cref="LinkResolver"/>), so a link met on the way is one swapped in since, and is not followed
    /// where the platform can tell.
    /// </summary>
    /// <param name="systemPath">The link-free path in the operating system's form (<see cref="SystemPath"/>).</param>
    /// <param name="appPath">The path as the caller gave it, for the sentences.</param>
    FileInventoryResult<IOpenedFile> Open(string systemPath, string appPath);
}
