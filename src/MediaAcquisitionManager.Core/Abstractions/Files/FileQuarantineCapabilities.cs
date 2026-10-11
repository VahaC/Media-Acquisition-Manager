namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>MAM-26 — what the platform the app runs on can do through <see cref="IFileQuarantine"/>.</summary>
/// <param name="CanRename">Whether it can rename a file without ever replacing or copying it: Linux's
/// <c>renameat2(RENAME_NOREPLACE)</c>. False elsewhere, where every write refuses rather than risk a copy.</param>
public sealed record FileQuarantineCapabilities(bool CanRename);
