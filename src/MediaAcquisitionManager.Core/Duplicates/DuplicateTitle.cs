namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>MAM-20 — a title and its year two or more subjects of a possible match share (ADR 0012's second
/// tier).</summary>
/// <param name="Text">The title as a library or a file name wrote it.</param>
/// <param name="Year">The year given with it.</param>
public sealed record DuplicateTitle(string Text, int Year);
