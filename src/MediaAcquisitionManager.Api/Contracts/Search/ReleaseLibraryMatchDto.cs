using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A library work of the search a release may be, and on what evidence (MAM-12).</summary>
/// <param name="WorkId">The work (<see cref="LibraryWorkDto.Id"/>), one of the search's
/// <see cref="SearchResponse.Library"/>.</param>
/// <param name="Evidence">The strongest evidence: <c>Identifier</c> (an id the listing gives), then
/// <c>TitleAndYear</c>, then <c>TitleOnly</c>.</param>
/// <param name="ConflictingSchemes">Every scheme, in scheme order, whose ids disagree: the listing gives one
/// the work doesn't hold, or two sources gave the work two.</param>
public sealed record ReleaseLibraryMatchDto(Guid WorkId, MatchEvidence Evidence, IReadOnlyList<MediaIdentifierScheme> ConflictingSchemes);
