using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>The outcome of <see cref="IIntegrationConnectionService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="IntegrationListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what the list doesn't take otherwise.</param>
public sealed record IntegrationListResult(IntegrationListStatus Status, IntegrationListResponse? List, string? Error)
{
    public static IntegrationListResult Listed(IntegrationListResponse list) => new(IntegrationListStatus.Listed, list, null);

    public static IntegrationListResult Invalid(string error) => new(IntegrationListStatus.Invalid, null, error);
}
