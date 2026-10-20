using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-25 — a media library that can be asked to look at given files now, and that tells what it holds at each file's
/// path: the optional capability the check of a replacement's new files stands on (specification §3.4, check 4, ADR 0035).
/// A further interface of the adapter class, found with <c>registry.Find&lt;IMediaLibraryFileLookup&gt;(adapterType)</c>
/// (ADR 0005); a library type without it can't confirm a new file, so a replacement through it stops for review.
/// </summary>
/// <remarks>
/// <para>
/// Reads only, but for one request: <see cref="RequestScanAsync"/> asks the library to look at the paths, as its own
/// watcher of the folders would when a file appears or goes, and asks for nothing else — no refresh of an item, no
/// metadata, no identification. It never touches a file. Everything <see cref="ReadFilesAsync"/> does is a read. Neither
/// is ever part of a library sync, which only reads.
/// </para>
/// <para>
/// MAM-26 — the same request tells the library that a replacement's old copies left it for the quarantine, or came back
/// (ADR 0030): no other member, since the library looks at a path whatever the change, and nothing more is written.
/// </para>
/// <para>
/// All methods return result types and never throw on expected failures (unreachable, refused, unexpected answer); only a
/// cancellation by the caller propagates, as <see cref="OperationCanceledException"/>. A caller's mistake — no paths, more
/// than <see cref="MaximumPaths"/> at once, a path that is blank, not absolute, longer than 4096 characters or holding a
/// control character, a change no <see cref="LibraryFileChange"/> names — throws an <see cref="ArgumentException"/>
/// before anything is sent. Every message is a <c>Message</c> the Api renders (ADR 0042), with the call's secrets already masked. The caller runs
/// <see cref="IIntegrationAdapter.TestConnectionAsync"/> first: the version gate (MAM-8).
/// </para>
/// <para>
/// A path is the library's own (<see cref="LibraryFile.ServicePath"/>), exactly as it reports one: the caller turns an app
/// path into it through the library's folders (ADR 0003), and nothing here trims or normalises it.
/// </para>
/// </remarks>
public interface IMediaLibraryFileLookup
{
    /// <summary>
    /// The most paths one call takes; more is a caller's mistake. A season pack is a few dozen files, but a whole series'
    /// pack — or new files read together with as many ticked copies — can name more, so a caller with more asks in batches
    /// of at most this many: asking again is harmless, and reading is only reading. The bound keeps one request's body to
    /// a couple of megabytes and the reads one call makes few enough that a runaway list fails at once instead of keeping
    /// the library busy for minutes.
    /// </summary>
    public const int MaximumPaths = 500;

    /// <summary>
    /// Asks the library to look at <paramref name="servicePaths"/> now, each as a path where <paramref name="change"/>
    /// happened — the files a download has just written, or an old copy put back, <see cref="LibraryFileChange.Created"/>;
    /// an old copy moved into the quarantine, <see cref="LibraryFileChange.Deleted"/> — in one request, and nothing else.
    /// Sent once, never retried. The library's answer says only that it took the request (Jellyfin answers at once and
    /// looks later, a wrong path included), so whether it found or dropped each file is <see cref="ReadFilesAsync"/>'s to
    /// tell; asking again is harmless. A <paramref name="change"/> that is no member is a caller's mistake.
    /// </summary>
    Task<IntegrationCallResult<LibraryScanRequest>> RequestScanAsync(
        IntegrationConnectionSettings settings, IReadOnlyList<string> servicePaths, LibraryFileChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the library holds at each of <paramref name="servicePaths"/>, one reading per path in the order asked: the
    /// file's own item — a version the library keeps hidden under another item included —, its kind, whether the library
    /// has finished reading it, its registry ids, and for an episode its place and its series' ids. A path the library
    /// doesn't list is a reading that says so, not a failure. Reads only.
    /// </summary>
    /// <remarks>
    /// A failure anywhere — the library unreachable, a read refused, an answer the app can't read — fails the whole call
    /// with that read's status and sentence and gives no reading at all: a library that couldn't be read never confirms a
    /// file.
    /// </remarks>
    Task<IntegrationCallResult<IReadOnlyList<LibraryFileReading>>> ReadFilesAsync(
        IntegrationConnectionSettings settings, IReadOnlyList<string> servicePaths, CancellationToken cancellationToken = default);
}
