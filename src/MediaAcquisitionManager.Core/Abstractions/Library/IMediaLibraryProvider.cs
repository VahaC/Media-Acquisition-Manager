using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// A media library the app reads: what the owner already has. First implemented for Jellyfin by
/// MAM-8, as a further interface of its <see cref="IIntegrationAdapter"/> class. Phase A members, and Phase B's
/// poster (MAM-35); Phase C adds a scan request with its capability (specification §3.4, check 4).
/// </summary>
/// <remarks>
/// <para>
/// All methods return result types and never throw on expected failures (unreachable, refused,
/// unexpected answer); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence with the
/// call's secrets already masked.
/// </para>
/// <para>
/// MAM-8 — a caller runs <see cref="IIntegrationAdapter.TestConnectionAsync"/> first: the test is
/// where an adapter refuses a server version it can't read
/// (<see cref="IntegrationCallStatus.UnsupportedVersion"/>), so nothing is read from a server whose
/// answers nobody has seen. The caller then reads <see cref="ListItemsAsync"/> in pages that overlap
/// by one item, until a page reaches <see cref="LibraryItemPage.TotalCount"/>. The service applies
/// <see cref="LibraryPageRequest.StartIndex"/> and <see cref="LibraryPageRequest.Limit"/> exactly
/// (Jellyfin does), so a short page before the end, or a first item that isn't the previous page's
/// last, means the library changed while it was being read.
/// </para>
/// <para>
/// MAM-32 — a library has three lists, read and counted one by one: its movies, its series and its
/// episodes (<see cref="LibraryPageRequest.Kind"/>). Each item says what it is, and an item of another
/// kind in a list means the list isn't what was asked for.
/// </para>
/// <para>
/// MAM-35 — a library work's card shows the library's own picture of it: each item names its current poster
/// (<see cref="LibraryItem.PosterTag"/>) and <see cref="GetPosterAsync"/> fetches that poster. Reading is what a library
/// provider does, so it is a member of this contract, not an optional capability (ADR 0005), and never a member of
/// <see cref="IMediaLibraryItemIdentifier"/>, whose contract names no image at all.
/// </para>
/// </remarks>
public interface IMediaLibraryProvider
{
    /// <summary>What this type of library can report.</summary>
    MediaLibraryCapabilities Capabilities { get; }

    /// <summary>
    /// One page of one of the library's lists in a stable order: exactly
    /// <see cref="LibraryPageRequest.Limit"/> items unless the page reaches the end, and the list's total
    /// count with every page.
    /// </summary>
    Task<IntegrationCallResult<LibraryItemPage>> ListItemsAsync(IntegrationConnectionSettings settings, LibraryPageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every library with its name and root folders: a sync decides which library holds each file from
    /// them (MAM-8, <c>LibraryLocationMatcher</c>), and MAM-11 checks destinations against them (ADR 0003,
    /// consequences).
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<LibraryLocation>>> ListLocationsAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-35 — the poster of the item <paramref name="sourceItemId"/> that <paramref name="posterTag"/> names (its
    /// <see cref="LibraryItem.PosterTag"/> as a list read it), as a JPEG the library resized to at most 342 pixels wide.
    /// <see cref="IntegrationCallStatus.NotFound"/> when the library has no such image or no longer knows the item. An id or
    /// a tag the library never gives is the caller's mistake: it throws an <see cref="ArgumentException"/> before anything
    /// is sent.
    /// </summary>
    Task<IntegrationCallResult<LibraryPoster>> GetPosterAsync(
        IntegrationConnectionSettings settings, string sourceItemId, string posterTag, CancellationToken cancellationToken = default);
}
