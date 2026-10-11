using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-27 — one work a replacement expects its new files to be, as the Download dialog showed it when the owner scheduled
/// (ADR 0032): what the job's page sets the new files' ids beside. Not <see cref="ReplacementWorkDto"/>, which is the
/// dialog's own row, offering copies to tick.
/// </summary>
/// <param name="WorkId">The work: a film, or a series for a TV release.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Title">Its title as the dialog showed it; <c>null</c> when it has none.</param>
/// <param name="Year">Its year as the dialog showed it; <c>null</c> when unknown.</param>
/// <param name="ConfirmedByOwner">The owner confirmed a match by title and year; <c>false</c> for a match by an id, which
/// needs none.</param>
/// <param name="Identifiers">Its registry ids then, as the app counts them — the owner's TMDb link applied —: what a new
/// file's ids are compared with. A series' are its show's.</param>
public sealed record ReplacementJobWorkDto(
    Guid WorkId,
    MediaWorkKind Kind,
    string? Title,
    int? Year,
    bool ConfirmedByOwner,
    IReadOnlyList<ReplacementJobIdentifierDto> Identifiers);
