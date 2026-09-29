using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>The outcome of a change to the owner's integrations.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Integration">The integration as it is now, when <see cref="IntegrationChangeStatus.Succeeded"/>
/// and the change leaves one to show.</param>
/// <param name="Error">The sentence for the owner when it failed with a reason to give.</param>
public sealed record IntegrationChangeResult(IntegrationChangeStatus Status, IntegrationDto? Integration, string? Error)
{
    public static IntegrationChangeResult NotFound { get; } = new(IntegrationChangeStatus.NotFound, null, null);

    public static IntegrationChangeResult Succeeded(IntegrationDto? integration = null) =>
        new(IntegrationChangeStatus.Succeeded, integration, null);

    public static IntegrationChangeResult Invalid(string error) => new(IntegrationChangeStatus.Invalid, null, error);

    public static IntegrationChangeResult NameTaken(string error) => new(IntegrationChangeStatus.NameTaken, null, error);

    public static IntegrationChangeResult AdapterUnavailable(string error) =>
        new(IntegrationChangeStatus.AdapterUnavailable, null, error);
}
