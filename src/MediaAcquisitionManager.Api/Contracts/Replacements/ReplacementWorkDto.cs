using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — one library work a release matched, with the copies it offers to replace (ADR 0032).</summary>
/// <param name="WorkId">The work: a film, or a series for a TV release.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Title">Its title as the Library shows it, else its original title; <c>null</c> when it has none.</param>
/// <param name="Year">Its year; <c>null</c> when unknown.</param>
/// <param name="Evidence">How the release matched it: by an id, by title and year, or by a title alone.</param>
/// <param name="ConflictingSchemes">The id schemes the release and the work disagree on, or the work holds two of.</param>
/// <param name="NeedsConfirmation">Matched by title and year without a conflict: a copy of it is replaced only with the
/// owner's tick of <paramref name="ConfirmationText"/>.</param>
/// <param name="ConfirmationText">The tick's own words — "This release is Alien (1979)" —, which the schedule stores
/// with the job as said.</param>
/// <param name="LibraryNames">The libraries holding its copies listed, in title order.</param>
/// <param name="Unavailable">Why none of its copies can be replaced; <c>null</c> when they may be.</param>
/// <param name="Copies">A film's every file of every version, in the Library's order; none for a series.</param>
/// <param name="Episodes">A series' held episodes the release claims, by season, then number, then title; none for a
/// film.</param>
public sealed record ReplacementWorkDto(
    Guid WorkId,
    MediaWorkKind Kind,
    string? Title,
    int? Year,
    MatchEvidence Evidence,
    IReadOnlyList<MediaIdentifierScheme> ConflictingSchemes,
    bool NeedsConfirmation,
    string ConfirmationText,
    IReadOnlyList<string> LibraryNames,
    ReplacementWorkUnavailableReason? Unavailable,
    IReadOnlyList<ReplacementCopyDto> Copies,
    IReadOnlyList<ReplacementEpisodeDto> Episodes);
