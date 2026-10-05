using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The reader for every other platform (MAM-9): size and modification time from .NET, and no identity. The
/// app ships for Linux and is developed on Windows; on, say, macOS a guessed identity would be worse than
/// none, because the cache trusts it (<see cref="FileInventoryCapabilities.ReportsFileIdentity"/>).
/// </summary>
/// <remarks>
/// Nothing is held open: ffprobe opens the checked path again by its text. Without an identity nothing read
/// here is kept, so a file swapped in meanwhile could only be shown once, never cached.
/// </remarks>
internal sealed class PortableFileStatusReader : IFileStatusReader
{
    /// <inheritdoc/>
    public bool ReportsFileIdentity => false;

    /// <inheritdoc/>
    public FileInventoryResult<IOpenedFile> Open(string systemPath, string appPath)
    {
        var described = Describe(systemPath, appPath);
        return described.Value is { } facts
            ? new FileInventoryResult<IOpenedFile>(FileInventoryStatus.Found, new OpenedFile(facts, systemPath), null)
            : FileSystemFailure.Relay<FileFacts, IOpenedFile>(described);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// MAM-18, ADR 0021 — listed again by its path, like a file is read again by it here, and with no identity: a
    /// link swapped in at the path since the checks is followed; the walk tells a loop by the folder's resolved
    /// path instead.
    /// </remarks>
    public FileInventoryResult<IOpenedFolder> OpenFolder(string systemPath, string appPath) =>
        new(FileInventoryStatus.Found, new FolderListedByPath(systemPath), null);

    private static FileInventoryResult<FileFacts> Describe(string systemPath, string appPath)
    {
        try
        {
            var file = new FileInfo(systemPath);
            var attributes = file.Attributes;
            if ((int)attributes == -1)
                return FileSystemFailure.Refused<FileFacts>(FileInventoryStatus.NotFound, FileInventoryMessages.NotFound(appPath));
            if (attributes.HasFlag(FileAttributes.Directory))
                return FileSystemFailure.Refused<FileFacts>(FileInventoryStatus.NotARegularFile, FileInventoryMessages.Folder(appPath));
            if (FileSystemFailure.RefuseUnlessReadable(systemPath, appPath) is { } refusal)
                return refusal;

            return new FileInventoryResult<FileFacts>(
                FileInventoryStatus.Found, new FileFacts(file.Length, file.LastWriteTimeUtc, null, null, null), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileSystemFailure.For<FileFacts>(exception, appPath);
        }
    }

    /// <summary>The checked path, asked again by its text.</summary>
    private sealed class OpenedFile(FileFacts facts, string systemPath) : IOpenedFile
    {
        public FileFacts Facts => facts;

        public string ProbePath => systemPath;

        public FileInventoryResult<FileFacts> ReadFactsAgain(string appPath) => Describe(systemPath, appPath);

        public void Dispose()
        {
        }
    }
}
