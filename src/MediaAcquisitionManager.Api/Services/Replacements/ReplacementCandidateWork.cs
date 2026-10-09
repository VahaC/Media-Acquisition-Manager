using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — one library work a release matched, with the copies it offers to replace (ADR 0032): a film's every file
/// of every version, or a series' held episodes the release claims.
/// </summary>
/// <param name="WorkId">The matched work: a film, or a series.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Title">Its title as the Library shows it, else its original title; <c>null</c> when it has none.</param>
/// <param name="Year">Its year; <c>null</c> when unknown.</param>
/// <param name="Match">How the release matched it, and which ids disagree.</param>
/// <param name="NeedsConfirmation">Matched by title and year without a conflict: a copy of it is replaced only with
/// the owner's tick.</param>
/// <param name="ConfirmationText">The tick's own words — "This release is Alien (1979)", "This release is Doctor Who
/// (2005), season 2" —, the one source of them for the dialog, the refusal and the audit row (R3).</param>
/// <param name="LibraryNames">The libraries holding the copies listed, each once, in title order.</param>
/// <param name="Unavailable">Why none of its copies can be replaced; <c>null</c> when they may be.</param>
/// <param name="Copies">A film's copies; none for a series.</param>
/// <param name="Episodes">A series' claimed held episodes, by season, then number, then title; none for a film.</param>
public sealed record ReplacementCandidateWork(
    Guid WorkId,
    MediaWorkKind Kind,
    string? Title,
    int? Year,
    WorkMatch Match,
    bool NeedsConfirmation,
    string ConfirmationText,
    IReadOnlyList<string> LibraryNames,
    ReplacementWorkUnavailableReason? Unavailable,
    IReadOnlyList<ReplacementCandidateCopy> Copies,
    IReadOnlyList<ReplacementCandidateEpisode> Episodes)
{
    /// <summary>Every copy it lists: a film's, then a series' episodes', in their order.</summary>
    public IEnumerable<ReplacementCandidateCopy> AllCopies => Copies.Concat(Episodes.SelectMany(episode => episode.Copies));

    /// <summary>Every copy of it the owner may tick: none while the work itself can't be replaced.</summary>
    public IEnumerable<ReplacementCandidateCopy> TickableCopies =>
        Unavailable is null ? AllCopies.Where(copy => copy.Unavailable is null) : [];
}
