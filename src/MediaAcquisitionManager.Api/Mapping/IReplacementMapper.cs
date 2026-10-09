using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>MAM-24 — turns what a release offers to replace, and what the download clients say of it, into the Download
/// dialog's contract (ADR 0031–0033).</summary>
public interface IReplacementMapper
{
    /// <summary>
    /// The dialog's answer: the release-level reason and its sentence, the release's edition words, each work with its
    /// copies in their order — each copy with the torrents <paramref name="holdings"/> found holding it, only when it can
    /// be ticked —, and how each download client was read.
    /// </summary>
    ReplacementOptionsResponse ToResponse(
        ReplacementUnavailableReason? unavailable,
        string? destinationProblem,
        IReadOnlyList<MediaEdition> releaseEditions,
        IReadOnlyList<ReplacementCandidateWork> works,
        ReplacementHoldings holdings);
}
