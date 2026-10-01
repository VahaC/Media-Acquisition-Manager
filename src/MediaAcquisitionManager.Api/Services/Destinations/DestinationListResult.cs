using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>The outcome of <see cref="IDestinationService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="DestinationListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what the list doesn't take otherwise.</param>
public sealed record DestinationListResult(DestinationListStatus Status, DestinationListResponse? List, string? Error)
{
    public static DestinationListResult Listed(DestinationListResponse list) => new(DestinationListStatus.Listed, list, null);

    public static DestinationListResult Invalid(string error) => new(DestinationListStatus.Invalid, null, error);
}
