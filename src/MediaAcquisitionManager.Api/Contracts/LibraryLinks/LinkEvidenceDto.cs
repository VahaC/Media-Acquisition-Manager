namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — what speaks for and against a TMDb suggestion (the core's <c>LinkEvidence</c>).</summary>
/// <param name="Title">How close its titles come to the work's.</param>
/// <param name="Year">The two years.</param>
/// <param name="Runtime">Its runtime against the work's measured durations.</param>
/// <param name="KindAgrees">Whether it is the kind of work the library work is.</param>
/// <param name="Genre">Its genres against the library's.</param>
/// <param name="Sibling">The linked neighbour it looks related to; <c>null</c> when none.</param>
public sealed record LinkEvidenceDto(
    TitleEvidenceDto Title,
    YearEvidenceDto Year,
    RuntimeEvidenceDto Runtime,
    bool KindAgrees,
    GenreEvidenceDto Genre,
    SiblingEvidenceDto? Sibling);
