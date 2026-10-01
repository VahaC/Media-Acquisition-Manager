using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// Turns what the file system refused into a <see cref="FileInventoryResult{T}"/> (MAM-9): the inventory
/// returns its expected failures instead of throwing them, and every reader and the link resolver words
/// them the same way.
/// </summary>
internal static class FileSystemFailure
{
    /// <summary>A refusal with its status and sentence.</summary>
    public static FileInventoryResult<T> Refused<T>(FileInventoryStatus status, string error) where T : class =>
        new(status, null, error);

    /// <summary>A refusal of one step carried on as the result of the next.</summary>
    public static FileInventoryResult<T> Relay<TFrom, T>(FileInventoryResult<TFrom> refusal) where TFrom : class where T : class =>
        new(refusal.Status, null, refusal.Error);

    /// <summary>
    /// The refusal an exception of a file system call stands for: a missing file or folder is
    /// <see cref="FileInventoryStatus.NotFound"/>, a denied one <see cref="FileInventoryStatus.Unreadable"/>, and
    /// anything else the system reported (a disk error, a lock) unreadable too, in the system's words.
    /// </summary>
    /// <param name="exception">An <see cref="IOException"/> or an <see cref="UnauthorizedAccessException"/>.</param>
    /// <param name="appPath">The path as the caller gave it, for the sentence.</param>
    public static FileInventoryResult<T> For<T>(Exception exception, string appPath) where T : class => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => Refused<T>(FileInventoryStatus.NotFound, FileInventoryMessages.NotFound(appPath)),
        UnauthorizedAccessException => Refused<T>(FileInventoryStatus.Unreadable, FileInventoryMessages.NotAllowedToRead(appPath)),
        _ => Refused<T>(FileInventoryStatus.Unreadable, FileInventoryMessages.CouldNotRead(appPath, exception.Message)),
    };

    /// <summary>
    /// Opens the file for reading and closes it again: the refusal when the app may not read it, or
    /// <c>null</c> when it can. Sharing read, write and delete, so the check never stands in the way of the
    /// download client finishing or moving the file.
    /// </summary>
    /// <param name="systemPath">The link-free path in the operating system's form.</param>
    /// <param name="appPath">The path as the caller gave it, for the sentence.</param>
    public static FileInventoryResult<FileFacts>? RefuseUnlessReadable(string systemPath, string appPath)
    {
        try
        {
            using var handle = File.OpenHandle(systemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return For<FileFacts>(exception, appPath);
        }
    }
}
