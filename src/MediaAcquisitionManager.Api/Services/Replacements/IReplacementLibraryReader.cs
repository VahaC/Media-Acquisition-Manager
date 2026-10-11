using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-25 — the media library's side of the check of a replacement's new files (specification §3.4, check 4, ADR 0035):
/// asking the library integration to look at the new paths, and reading what it holds at a path — the file's own item,
/// its kind, whether the library finished reading it, its ids and an episode's place. MAM-26 — and telling it that the
/// old copies went into the quarantine or came back (ADR 0030).
/// </summary>
/// <remarks>
/// <para>
/// Through the optional capability <c>IMediaLibraryFileLookup</c> (ADR 0005): a library type without it can never
/// confirm a file, which is the owner's review at once. A library that can't be asked now — switched off, a secret the
/// app can't open, unreachable, failing its version gate — is a wait, never a pass: an unavailable library never
/// confirms a file. The version gate (MAM-8) runs before each call's requests, as before every sync.
/// </para>
/// <para>
/// One request writes nothing in the library: the request to look at the paths — to ask about new files, or to tell of
/// old copies that went or came back —, one per batch of at most <c>IMediaLibraryFileLookup.MaximumPaths</c> paths, each
/// sent once and never retried. Everything else is a read, in
/// batches alike. Never throws but for the caller's cancellation; a call the adapter refuses as a caller's mistake is the
/// app's own fault, which no wait cures: the owner's review at once.
/// </para>
/// </remarks>
public interface IReplacementLibraryReader
{
    /// <summary>
    /// Asks the library integration <paramref name="integrationId"/> to look at <paramref name="libraryPaths"/>, its own
    /// paths of the new files, in one request per batch. Its wait's reason is worded for the files' wait, from the check's
    /// start.
    /// </summary>
    Task<ReplacementLibraryAnswer> AskAsync(Guid integrationId, IReadOnlyList<string> libraryPaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the library integration <paramref name="integrationId"/> holds at each of <paramref name="libraryPaths"/>,
    /// in their order, whatever the batches. Its wait's reason is worded for the library's wait, from when it was asked to
    /// look.
    /// </summary>
    Task<ReplacementLibraryAnswer> ReadAsync(Guid integrationId, IReadOnlyList<string> libraryPaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-26 — tells the library integration <paramref name="integrationId"/> that <paramref name="change"/> happened at
    /// <paramref name="libraryPaths"/>, its own paths of a replacement's old copies — they went into the quarantine
    /// (<see cref="LibraryFileChange.Deleted"/>) or came back (<see cref="LibraryFileChange.Created"/>) —, in one request
    /// per batch, through the same gates as asking. Its reasons are worded for the notice, which never stops a job.
    /// </summary>
    Task<ReplacementLibraryAnswer> TellAsync(
        Guid integrationId, IReadOnlyList<string> libraryPaths, LibraryFileChange change, CancellationToken cancellationToken = default);
}
