using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>The outcome of <see cref="IAllowedRootsService.UpdateAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Error">Why, as a message the Api renders (ADR 0042), when <see cref="AllowedRootsUpdateStatus.Invalid"/>.</param>
public sealed record AllowedRootsUpdateResult(AllowedRootsUpdateStatus Status, Message? Error)
{
    public static AllowedRootsUpdateResult Saved { get; } = new(AllowedRootsUpdateStatus.Saved, null);

    public static AllowedRootsUpdateResult Invalid(Message error) => new(AllowedRootsUpdateStatus.Invalid, error);
}
