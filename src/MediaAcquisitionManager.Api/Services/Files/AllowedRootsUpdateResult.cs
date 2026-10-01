namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>The outcome of <see cref="IAllowedRootsService.UpdateAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Error">The rule's sentence when <see cref="AllowedRootsUpdateStatus.Invalid"/>.</param>
public sealed record AllowedRootsUpdateResult(AllowedRootsUpdateStatus Status, string? Error)
{
    public static AllowedRootsUpdateResult Saved { get; } = new(AllowedRootsUpdateStatus.Saved, null);

    public static AllowedRootsUpdateResult Invalid(string error) => new(AllowedRootsUpdateStatus.Invalid, error);
}
