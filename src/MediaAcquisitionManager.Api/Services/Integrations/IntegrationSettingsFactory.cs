using System.Diagnostics.CodeAnalysis;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <inheritdoc cref="IIntegrationSettingsFactory"/>
/// <remarks>
/// MAM-8 — lifted out of the connection test's Keep path, so the test and a library sync open a stored
/// secret with one implementation, one sentence and one log line.
/// </remarks>
public sealed class IntegrationSettingsFactory(IEncryptionService encryption, ILogger<IntegrationSettingsFactory> logger)
    : IIntegrationSettingsFactory
{
    public IntegrationSettingsResult Create(IntegrationConnectionEntity integration)
    {
        if (!TryReadSecret(integration, IntegrationSecretField.Password, out var password, out var passwordError))
            return IntegrationSettingsResult.SecretUnreadable(passwordError);
        if (!TryReadSecret(integration, IntegrationSecretField.ApiKey, out var apiKey, out var apiKeyError))
            return IntegrationSettingsResult.SecretUnreadable(apiKeyError);

        // MAM-46 — a metadata catalog's language travels with every call, so its adapter asks in it.
        return IntegrationSettingsResult.Built(
            new IntegrationConnectionSettings(integration.Id, integration.BaseUrl, integration.UserName, password, apiKey, integration.CatalogLanguage));
    }

    // Decrypted with the purpose it was sealed for, so a value copied from another row or column fails
    // here instead of being sent to this row's server.
    public bool TryReadSecret(
        IntegrationConnectionEntity integration,
        IntegrationSecretField field,
        out string? secret,
        [NotNullWhen(false)] out string? error)
    {
        secret = null;
        error = null;
        var sealedValue = field == IntegrationSecretField.Password ? integration.PasswordEncrypted : integration.ApiKeyEncrypted;
        if (sealedValue is null)
            return true;
        if (encryption.TryDecrypt(sealedValue, IntegrationSecretPurpose.For(integration.Id, field), out secret))
            return true;

        var name = SecretFieldName(field);
        // Warning, so it ships off the box: an operator must act (a changed key, a restored or edited
        // database). Neither the value nor the key is named.
        logger.LogWarning(
            "The stored {SecretField} of integration {IntegrationId} could not be decrypted with the configured encryption key.",
            name,
            integration.Id);
        error = $"The stored {name} of “{integration.Name}” can't be read with this installation's encryption key. Enter the {name} again and save.";
        return false;
    }

    private static string SecretFieldName(IntegrationSecretField field) =>
        field == IntegrationSecretField.Password ? "password" : "API key";
}
