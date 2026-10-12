namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-26 — one quarantine on the Media folders page, with the room it takes (ADR 0030: "The app shows how much
/// room the quarantine takes"), from the replacements' own rows, never a walk of the disk.</summary>
/// <param name="Path">The quarantine folder, canonical: <c>&lt;allowed root&gt;/.mam-quarantine</c>.</param>
/// <param name="ReplacementCount">How many replacements hold names in it.</param>
/// <param name="RemovalCount">MAM-40 — how many of the owner's removals of a duplicate copy hold names in it.</param>
/// <param name="FileCount">How many names it holds.</param>
/// <param name="SizeBytes">Their sizes added up, as each was before its move.</param>
public sealed record QuarantineSpaceDto(string Path, int ReplacementCount, int RemovalCount, int FileCount, long SizeBytes);
