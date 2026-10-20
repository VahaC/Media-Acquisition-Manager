using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>The outcome of <see cref="IIntegrationConnectionService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="IntegrationListStatus.Listed"/>.</param>
/// <param name="Error">What the list doesn't take otherwise, as a message the Api renders (ADR 0042).</param>
public sealed record IntegrationListResult(IntegrationListStatus Status, IntegrationListResponse? List, Message? Error)
{
    public static IntegrationListResult Listed(IntegrationListResponse list) => new(IntegrationListStatus.Listed, list, null);

    public static IntegrationListResult Invalid(Message error) => new(IntegrationListStatus.Invalid, null, error);
}
