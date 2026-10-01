using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Releases;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>Turns what the release sources answered a search into its contract (MAM-10).</summary>
public interface IReleaseSearchMapper
{
    /// <summary>
    /// One source's answer, its hits paired in order with the ids of the rows they were stored as
    /// (<paramref name="releaseIds"/>), each category named from the source's capabilities, and the
    /// quality and languages each title claims. The sentence of a failure only when
    /// <paramref name="forOwner"/>: it can name the service's address.
    /// </summary>
    ReleaseSourceResultDto ToDto(ReleaseSourceOutcome outcome, IReadOnlyList<Guid> releaseIds, bool forOwner);
}
