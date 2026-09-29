using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>
/// Finds which works a title or an identifier may name. First implemented from media-server data by
/// MAM-12, as a further interface of an <see cref="IIntegrationAdapter"/> class. Phase A members only.
/// </summary>
/// <remarks>
/// All methods return result types and never throw on expected failures (unreachable, refused,
/// unexpected answer); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence with the
/// call's secrets already masked.
/// </remarks>
public interface IMetadataResolver
{
    /// <summary>What this type can look up by.</summary>
    MetadataResolverCapabilities Capabilities { get; }

    /// <summary>
    /// Every candidate with its evidence; it never picks one. A title and year match alone never
    /// authorises a replacement (specification §3.1).
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<MetadataCandidate>>> LookupAsync(IntegrationConnectionSettings settings, MetadataQuery query, CancellationToken cancellationToken = default);
}
