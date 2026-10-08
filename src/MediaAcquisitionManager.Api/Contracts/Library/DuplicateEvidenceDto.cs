namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — why a duplicate group is one: each class fills its own part, the rest is <c>null</c> or
/// empty.</summary>
/// <param name="Identity">One file's names: the file they lead to.</param>
/// <param name="Sha256">Byte-identical files: the SHA-256 of their bytes.</param>
/// <param name="Identifiers">Copies of one work: the registry ids two works or more share, or a single work's own.</param>
/// <param name="Titles">A possible match: the titles and years two subjects or more share.</param>
/// <param name="DurationsAgree">A possible match: whether the measured durations agree (2 minutes or 10 % of the
/// longest); <c>null</c> when fewer than two are known, or for another class.</param>
/// <param name="RunningTimes">MAM-37 — copies of one work: the running times of the shortest and the longest measured
/// copy, whether or not they differ widely; <c>null</c> when fewer than two copies have one, or for another
/// class.</param>
public sealed record DuplicateEvidenceDto(
    DuplicateIdentityDto? Identity,
    string? Sha256,
    IReadOnlyList<DuplicateIdentifierDto> Identifiers,
    IReadOnlyList<DuplicateTitleDto> Titles,
    bool? DurationsAgree,
    DuplicateRunningTimesDto? RunningTimes);
