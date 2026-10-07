using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — a library work holding a duplicate group's file, through one of its versions.</summary>
/// <param name="Id">The work.</param>
/// <param name="Kind">What the library says it is.</param>
/// <param name="Title">The title its Library card shows: a displayed one, else an original one; <c>null</c> when it has
/// none.</param>
/// <param name="Year">The year given with that title; <c>null</c> when none.</param>
/// <param name="Episode">An episode's place, as its Library card shows it; <c>null</c> for a film.</param>
/// <param name="SeriesTitle">The title of that place's series; <c>null</c> when there is none.</param>
/// <param name="VersionId">The version the file is a file of.</param>
/// <param name="EditionLabel">That version's name, as the library gave it; <c>null</c> when none.</param>
/// <param name="SourceName">The integration that reported that version.</param>
/// <param name="Identifiers">The work's ids as every reader counts them (MAM-34 links applied), with where each came
/// from — so files of works whose ids disagree each show their own.</param>
public sealed record LibraryDuplicateWorkDto(
    Guid Id,
    MediaWorkKind Kind,
    string? Title,
    int? Year,
    LibraryEpisodeDto? Episode,
    string? SeriesTitle,
    Guid VersionId,
    string? EditionLabel,
    string? SourceName,
    IReadOnlyList<LibraryIdentifierDto> Identifiers);
