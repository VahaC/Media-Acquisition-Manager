using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A library work a search lists, and on what evidence (MAM-12): one it matches, or one holding
/// every word of its title.</summary>
/// <remarks>
/// Evidence to show, never a decision (ADR 0012): every match is listed, and a conflict is flagged, never
/// resolved. Which source claimed what stays visible (ADR 0006).
/// </remarks>
/// <param name="Work">The work as the Library page shows it, its versions, files and what the app read
/// from them included. MAM-32 — a movie, a series with all its seasons, or an episode of no series; never
/// an episode of a series, which counts for its series.</param>
/// <param name="Evidence">The strongest evidence: <c>Identifier</c>, then <c>TitleAndYear</c>, then
/// <c>TitleOnly</c> — a candidate to check by hand; or <c>Words</c>, not a match: one title or one file
/// path of the work holds every word of the title asked for (ADR 0012). MAM-32 — a series' the strongest
/// of its own and of the episodes the search found.</param>
/// <param name="MatchedIdentifiers">The work's ids that equal one asked for, each with the source that
/// claimed it, by scheme, value, then source; empty unless <paramref name="Evidence"/> is
/// <c>Identifier</c>. MAM-32 — a series' episode's, when an id of the episode is the evidence.</param>
/// <param name="MatchedTitle">The work's title that matched the title asked for — for <c>Words</c>, the
/// title holding its words; <c>null</c> when only an id matched, or only a file path holds the
/// words. MAM-32 — a series' episode's title, when the episode's evidence is the one shown.</param>
/// <param name="ConflictingSchemes">Every scheme, in scheme order, whose ids disagree: the search asked for
/// one the work doesn't hold, or two sources gave the work two.</param>
public sealed record LibraryMatchDto(
    LibraryWorkDto Work,
    MatchEvidence Evidence,
    IReadOnlyList<LibraryIdentifierDto> MatchedIdentifiers,
    LibraryTitleDto? MatchedTitle,
    IReadOnlyList<MediaIdentifierScheme> ConflictingSchemes);
