using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// A media library the app reads: what the owner already has. First implemented for Jellyfin by
/// MAM-8, as a further interface of its <see cref="IIntegrationAdapter"/> class. Phase A members only;
/// Phase C adds a scan request with its capability (specification §3.4, check 4).
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
}
