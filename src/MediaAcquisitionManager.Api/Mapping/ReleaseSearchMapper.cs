using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IReleaseSearchMapper"/>
public sealed class ReleaseSearchMapper : IReleaseSearchMapper
{
    public ReleaseSourceResultDto ToDto(ReleaseSourceOutcome outcome, IReadOnlyList<Guid> releaseIds, bool forOwner)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(releaseIds.Count, outcome.Hits.Count, nameof(releaseIds));

        // A source can repeat a category id in its capabilities (Toloka's repeats 100137): the first name
        // wins, as the adapter keeps the first.
        var categoryNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in outcome.Categories)
            categoryNames.TryAdd(category.Id, category.Name);

        return new ReleaseSourceResultDto(
            outcome.IntegrationId,
            outcome.Name,
            outcome.Status,
            outcome.NotSearchedReason,
            forOwner ? outcome.Error : null,
            [.. outcome.Hits.Select((hit, index) => ToDto(releaseIds[index], hit, categoryNames))]);
    }

    public ClaimedReleaseQualityDto ToDto(ClaimedReleaseQuality claims) =>
        new(
            claims.Resolution,
            claims.Medium,
            claims.VideoCodec,
            claims.DynamicRanges,
            claims.AudioLanguages,
            claims.SubtitleLanguages);

    private FoundReleaseDto ToDto(Guid releaseId, ReleaseSearchHit hit, IReadOnlyDictionary<string, string> categoryNames) =>
        new(
            releaseId,
            hit.Title,
            hit.SizeBytes,
            hit.Seeders,
            hit.Peers,
            hit.PublishedUtc,
            hit.SourcePageUrl,
            [.. hit.CategoryIds.Select(id => new ReleaseCategoryDto(id, categoryNames.GetValueOrDefault(id)))],
            hit.PayloadKind,
            // Read from the title every time it is shown (specification §3.1: claimed, never verified).
            ToDto(ReleaseTitleClaims.Read(hit.Title)));
}
