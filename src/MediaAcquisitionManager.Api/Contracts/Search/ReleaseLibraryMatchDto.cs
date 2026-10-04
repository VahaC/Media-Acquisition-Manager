using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A library work of the search a release may be, and on what evidence (MAM-12).</summary>
/// <param name="WorkId">The work (<see cref="LibraryWorkDto.Id"/>), one of the search's
/// <see cref="SearchResponse.Library"/> — those it lists by their words included. MAM-32 — a series for a
/// TV release, a movie for a movie release, never the other (ADR 0020).</param>
/// <param name="Evidence">The strongest evidence: <c>Identifier</c> (an id the listing gives), then
/// <c>TitleAndYear</c>, then <c>TitleOnly</c>. Never <c>Words</c>: a release is never matched by its words
/// (MAM-12, ADR 0012).</param>
/// <param name="ConflictingSchemes">Every scheme, in scheme order, whose ids disagree: the listing gives one
/// the work doesn't hold, or two sources gave the work two.</param>
/// <param name="HeldEpisodeCount">MAM-32 — on a TV release's match, how many episodes of the series the
/// library holds among the seasons or episodes the release's title claims
/// (<see cref="ReleaseWorkClaimDto.Episodes"/>); <c>null</c> on a movie's, and when the title claims
/// none.</param>
public sealed record ReleaseLibraryMatchDto(
    Guid WorkId,
    MatchEvidence Evidence,
    IReadOnlyList<MediaIdentifierScheme> ConflictingSchemes,
    int? HeldEpisodeCount);
