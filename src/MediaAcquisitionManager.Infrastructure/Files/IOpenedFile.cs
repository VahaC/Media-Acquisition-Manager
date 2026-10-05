using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// A regular file the platform's reader has opened for reading after the checks (MAM-9, ADR 0009): its facts,
/// the path that leads ffprobe to this very file, and (MAM-19) its bytes. Kept open while ffprobe or the hash
/// reads, so a link swapped in at the checked path meanwhile can't put another file in its place.
/// </summary>
internal interface IOpenedFile : IDisposable
{
    /// <summary>The facts of the file as it was opened.</summary>
    FileFacts Facts { get; }

    /// <summary>What ffprobe is told to open (after <c>file:</c>) to read the file opened here.</summary>
    string ProbePath { get; }

    /// <summary>
    /// The facts of the same open file asked again, after ffprobe read it: <see cref="FileInventoryStatus.Found"/>,
    /// or why they couldn't be read. A size or a modification time that moved means it was written meanwhile.
    /// </summary>
    /// <param name="appPath">The path as the caller gave it, for the sentences.</param>
    FileInventoryResult<FileFacts> ReadFactsAgain(string appPath);

    /// <summary>
    /// MAM-19 — reads the bytes of the file opened here, from <paramref name="offset"/> into
    /// <paramref name="buffer"/>: how many it read, 0 at the end. Where the reader holds the file open, from
    /// that very file, never by its path again.
    /// </summary>
    /// <exception cref="IOException">The system couldn't read it (a disk error).</exception>
    ValueTask<int> ReadAsync(Memory<byte> buffer, long offset, CancellationToken cancellationToken = default);
}
