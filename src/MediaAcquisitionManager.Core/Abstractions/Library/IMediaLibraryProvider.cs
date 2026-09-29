using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// A media library the app reads: what the owner already has. First implemented for Jellyfin by
/// MAM-8, as a further interface of its <see cref="IIntegrationAdapter"/> class. Phase A members only;
/// Phase C adds a scan request with its capability (specification §3.4, check 4).
/// </summary>
/// <remarks>
/// All methods return result types and never throw on expected failures (unreachable, refused,
/// unexpected answer); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence with the
/// call's secrets already masked.
/// </remarks>
public interface IMediaLibraryProvider
{
    /// <summary>What this type of library can report.</summary>
    MediaLibraryCapabilities Capabilities { get; }

    /// <summary>One page of the library's items.</summary>
    Task<IntegrationCallResult<LibraryItemPage>> ListItemsAsync(IntegrationConnectionSettings settings, LibraryPageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// The library's root folders, for path-mapping coverage and MAM-11's destination check (ADR 0003,
    /// consequences).
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<LibraryLocation>>> ListLocationsAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default);
}
