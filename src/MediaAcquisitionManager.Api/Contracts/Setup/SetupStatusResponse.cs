namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Whether this installation still needs its first-run setup.</summary>
/// <param name="IsRequired">True until the installation owner exists; then false for good.</param>
public sealed record SetupStatusResponse(bool IsRequired);
