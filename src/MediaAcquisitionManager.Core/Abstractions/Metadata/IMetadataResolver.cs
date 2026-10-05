using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>
/// Finds which works a title or an identifier may name, as a further interface of an
/// <see cref="IIntegrationAdapter"/> class. MAM-12 matches a search and its releases against the library
/// snapshot MAM-8 imported, which needs no call to the media server. MAM-33 — implemented by the metadata
/// catalog's adapter (TMDb) beside <see cref="IMetadataCatalog"/>, and used only to find which catalog work
/// a library work without a catalog id, or a new release, may be — for recommendations, never for the
/// library (ADR 0025).
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
