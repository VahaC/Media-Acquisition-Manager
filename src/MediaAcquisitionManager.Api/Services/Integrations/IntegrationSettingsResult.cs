using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>The outcome of <see cref="IIntegrationSettingsFactory.Create"/>.</summary>
/// <remarks>
/// Safe to print: the generated <c>ToString</c> prints <see cref="Settings"/> through its own, which
/// masks both secrets.
/// </remarks>
/// <param name="Status">Whether the settings were built.</param>
/// <param name="Settings">The settings, secrets decrypted, when <see cref="IntegrationSettingsStatus.Built"/>.</param>
/// <param name="Error">The sentence for the owner otherwise, as a message the Api renders (ADR 0042).</param>
public sealed record IntegrationSettingsResult(IntegrationSettingsStatus Status, IntegrationConnectionSettings? Settings, Message? Error)
{
    public static IntegrationSettingsResult Built(IntegrationConnectionSettings settings) =>
        new(IntegrationSettingsStatus.Built, settings, null);

    public static IntegrationSettingsResult SecretUnreadable(Message error) =>
        new(IntegrationSettingsStatus.SecretUnreadable, null, error);
}
