namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// A folder the Windows and the portable readers hand the walk (MAM-18): nothing held open, listed again by its
/// path, and no identity, so the walk tells a loop by the folder's resolved path (ADR 0021).
/// </summary>
/// <param name="listPath">The folder's link-free path in the operating system's form (<see cref="SystemPath"/>).</param>
internal sealed class FolderListedByPath(string listPath) : IOpenedFolder
{
    /// <inheritdoc/>
    public string ListPath => listPath;

    /// <inheritdoc/>
    public long? DeviceId => null;

    /// <inheritdoc/>
    public long? Inode => null;

    public void Dispose()
    {
    }
}
