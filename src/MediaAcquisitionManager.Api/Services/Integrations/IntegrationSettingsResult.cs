using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>The outcome of <see cref="IIntegrationSettingsFactory.Create"/>.</summary>
/// <remarks>
/// Safe to print: the generated <c>ToString</c> prints <see cref="Settings"/> through its own, which
/// masks both secrets.
/// </remarks>
/// <param name="Status">Whether the settings were built.</param>
/// <param name="Settings">The settings, secrets decrypted, when <see cref="IntegrationSettingsStatus.Built"/>.</param>
/// <param name="Error">The sentence for the owner otherwise.</param>
public sealed record IntegrationSettingsResult(IntegrationSettingsStatus Status, IntegrationConnectionSettings? Settings, string? Error)
{
    public static IntegrationSettingsResult Built(IntegrationConnectionSettings settings) =>
        new(IntegrationSettingsStatus.Built, settings, null);

    public static IntegrationSettingsResult SecretUnreadable(string error) =>
        new(IntegrationSettingsStatus.SecretUnreadable, null, error);
}
