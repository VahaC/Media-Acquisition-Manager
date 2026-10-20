using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>The outcome of a change to the destinations (MAM-11).</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Destination">The destination as it is now, when <see cref="DestinationChangeStatus.Succeeded"/>
/// and the change leaves one to show.</param>
/// <param name="Error">The sentence for the owner when it failed with a reason to give.</param>
public sealed record DestinationChangeResult(DestinationChangeStatus Status, DestinationDto? Destination, Message? Error)
{
    public static DestinationChangeResult NotFound { get; } = new(DestinationChangeStatus.NotFound, null, null);

    public static DestinationChangeResult Succeeded(DestinationDto? destination = null) =>
        new(DestinationChangeStatus.Succeeded, destination, null);

    public static DestinationChangeResult Invalid(Message error) => new(DestinationChangeStatus.Invalid, null, error);

    public static DestinationChangeResult DownloadClientMissing(Message error) =>
        new(DestinationChangeStatus.DownloadClientMissing, null, error);

    public static DestinationChangeResult NameTaken(Message error) => new(DestinationChangeStatus.NameTaken, null, error);

    public static DestinationChangeResult ClientUnusable(Message error) => new(DestinationChangeStatus.ClientUnusable, null, error);

    public static DestinationChangeResult CategoryMissing(Message error) => new(DestinationChangeStatus.CategoryMissing, null, error);

    public static DestinationChangeResult ClientFailed(Message error) => new(DestinationChangeStatus.ClientFailed, null, error);

    public static DestinationChangeResult SourceMissing(Message error) => new(DestinationChangeStatus.SourceMissing, null, error);
}
