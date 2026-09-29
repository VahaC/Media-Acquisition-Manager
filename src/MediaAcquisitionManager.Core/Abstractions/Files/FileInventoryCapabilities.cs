namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>What the platform the app runs on can report about a file.</summary>
/// <param name="ReportsFileIdentity">Whether it gives a device and inode. False where the platform has
/// none: an unknown identity beats a guessed one.</param>
public sealed record FileInventoryCapabilities(bool ReportsFileIdentity);
