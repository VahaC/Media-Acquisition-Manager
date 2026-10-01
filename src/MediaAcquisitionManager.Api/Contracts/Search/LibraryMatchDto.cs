using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A library work a search matches, and on what evidence (MAM-12).</summary>
/// <remarks>
/// Evidence to show, never a decision (ADR 0012): every match is listed, and a conflict is flagged, never
/// resolved. Which source claimed what stays visible (ADR 0006).
/// </remarks>
/// <param name="Work">The work as the Library page shows it, its versions, files and what the app read
/// from them included.</param>
/// <param name="Evidence">The strongest evidence: <c>Identifier</c>, then <c>TitleAndYear</c>, then
/// <c>TitleOnly</c> — a candidate to check by hand.</param>
/// <param name="MatchedIdentifiers">The work's ids that equal one asked for, each with the source that
/// claimed it, by scheme, value, then source; empty unless <paramref name="Evidence"/> is
/// <c>Identifier</c>.</param>
/// <param name="MatchedTitle">The work's title that matched the title asked for; <c>null</c> when only an
/// id matched.</param>
/// <param name="ConflictingSchemes">Every scheme, in scheme order, whose ids disagree: the search asked for
/// one the work doesn't hold, or two sources gave the work two.</param>
public sealed record LibraryMatchDto(
    LibraryWorkDto Work,
    MatchEvidence Evidence,
    IReadOnlyList<LibraryIdentifierDto> MatchedIdentifiers,
    LibraryTitleDto? MatchedTitle,
    IReadOnlyList<MediaIdentifierScheme> ConflictingSchemes);
