using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>The outcome of a change to the destinations (MAM-11).</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Destination">The destination as it is now, when <see cref="DestinationChangeStatus.Succeeded"/>
/// and the change leaves one to show.</param>
/// <param name="Error">The sentence for the owner when it failed with a reason to give.</param>
public sealed record DestinationChangeResult(DestinationChangeStatus Status, DestinationDto? Destination, string? Error)
{
    public static DestinationChangeResult NotFound { get; } = new(DestinationChangeStatus.NotFound, null, null);

    public static DestinationChangeResult Succeeded(DestinationDto? destination = null) =>
        new(DestinationChangeStatus.Succeeded, destination, null);

    public static DestinationChangeResult Invalid(string error) => new(DestinationChangeStatus.Invalid, null, error);

    public static DestinationChangeResult DownloadClientMissing(string error) =>
        new(DestinationChangeStatus.DownloadClientMissing, null, error);

    public static DestinationChangeResult NameTaken(string error) => new(DestinationChangeStatus.NameTaken, null, error);

    public static DestinationChangeResult ClientUnusable(string error) => new(DestinationChangeStatus.ClientUnusable, null, error);

    public static DestinationChangeResult CategoryMissing(string error) => new(DestinationChangeStatus.CategoryMissing, null, error);

    public static DestinationChangeResult ClientFailed(string error) => new(DestinationChangeStatus.ClientFailed, null, error);
}
