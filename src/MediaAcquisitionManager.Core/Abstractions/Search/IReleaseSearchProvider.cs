using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>
/// A source the app searches for releases. Implemented for Torznab by MAM-10, as a further interface of
/// its <see cref="IIntegrationAdapter"/> class. Phase A members only.
/// </summary>
/// <remarks>
/// <para>
/// Capabilities are a call, not a property: a feed's capabilities differ per indexer behind one
/// gateway, so they belong to a connection, not to the type.
/// </para>
/// <para>
/// All methods return result types and never throw on expected failures (unreachable, refused,
/// unexpected answer); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence with the
/// call's secrets already masked.
/// </para>
/// </remarks>
public interface IReleaseSearchProvider
{
    /// <summary>What this connection's source can search by, and its categories.</summary>
    Task<IntegrationCallResult<ReleaseSearchCapabilities>> GetCapabilitiesAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// The releases the source lists for <paramref name="query"/>, in the source's own order, at most
    /// <see cref="ReleaseSearchQuery.Limit"/> of them and only those listed in one of its categories
    /// (or in none at all).
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>> SearchAsync(IntegrationConnectionSettings settings, ReleaseSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a release's payload on the server when it is submitted. The locator is only ever one
    /// this connection returned, and it never reaches the browser (ADR 0004); a locator that would send
    /// the request anywhere but the source's own address is refused without a request (MAM-10).
    /// </summary>
    Task<IntegrationCallResult<ReleasePayload>> FetchPayloadAsync(IntegrationConnectionSettings settings, ReleasePayloadLocator locator, CancellationToken cancellationToken = default);
}
