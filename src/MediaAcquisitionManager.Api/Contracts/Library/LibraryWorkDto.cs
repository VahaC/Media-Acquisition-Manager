using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One work of the Library page with what the libraries reported about it.</summary>
/// <remarks>
/// MAM-8 — a value no library reported is <c>null</c>, never a guess: the page shows it as unknown.
/// </remarks>
/// <param name="Id">The work's id; the same across syncs while its items stay.</param>
/// <param name="Kind">What the work is.</param>
/// <param name="Title">The displayed title a library gave; with several, the first in title order.
/// <c>null</c> when none was given.</param>
/// <param name="OriginalTitle">The original title a library gave, chosen the same way; <c>null</c> when
/// none was given.</param>
/// <param name="Year">The year given with <paramref name="Title"/>, else with
/// <paramref name="OriginalTitle"/>; <c>null</c> when neither came with one.</param>
/// <param name="Identifiers">Every IMDb and TMDb id a source claims for it, by scheme, then value.</param>
/// <param name="MissingIdentifiers">The schemes it has no id of; a work needs both to be matched
/// reliably (specification §3.1).</param>
/// <param name="Versions">Its versions with their files.</param>
public sealed record LibraryWorkDto(
    Guid Id,
    MediaWorkKind Kind,
    string? Title,
    string? OriginalTitle,
    int? Year,
    IReadOnlyList<LibraryIdentifierDto> Identifiers,
    IReadOnlyList<MediaIdentifierScheme> MissingIdentifiers,
    IReadOnlyList<LibraryVersionDto> Versions);
