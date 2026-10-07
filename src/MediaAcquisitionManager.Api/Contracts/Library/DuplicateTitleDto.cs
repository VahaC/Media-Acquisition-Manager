namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — a title and its year the subjects of a possible match share (ADR 0012).</summary>
/// <param name="Text">The title as a library or a file name wrote it.</param>
/// <param name="Year">The year given with it.</param>
public sealed record DuplicateTitleDto(string Text, int Year);
