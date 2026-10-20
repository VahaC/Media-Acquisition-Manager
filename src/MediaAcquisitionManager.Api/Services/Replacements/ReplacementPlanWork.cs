using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — one matched work a scheduled replacement replaces copies of, as the dialog showed it
/// (<c>ReplacementJobWorkEntity</c>): what the new file is expected to be (ADR 0032).
/// </summary>
/// <param name="WorkId">The film or the series.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Title">Its title as the dialog showed it.</param>
/// <param name="Year">Its year.</param>
/// <param name="Evidence">How the release matched it.</param>
/// <param name="ConfirmedByOwner">The owner ticked its confirmation, which a match by title and year needs.</param>
/// <param name="ConfirmationText">The confirmation's own words, as the claim they say (MAM-47).</param>
/// <param name="Identifiers">Its registry ids as the app's readers count them now, the owner's own TMDb links
/// included.</param>
public sealed record ReplacementPlanWork(
    Guid WorkId,
    MediaWorkKind Kind,
    string? Title,
    int? Year,
    MatchEvidence Evidence,
    bool ConfirmedByOwner,
    Message ConfirmationText,
    IReadOnlyList<ReplacementPlanIdentifier> Identifiers);
