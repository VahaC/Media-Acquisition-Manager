using System.Diagnostics.CodeAnalysis;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Integrations;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Integrations;

// The connection tests: a saved integration's, and an unsaved draft's from the Add or Edit dialog.
// Neither writes anything — not even an audit row, as a test changes nothing — and both answer
// Tested whatever the service said (ADR 0005).
public sealed partial class IntegrationConnectionService
{
    private const string EditedIntegrationMissingError =
        "The integration you are editing no longer exists. Close the dialog and reload the page.";

    public async Task<IntegrationTestResult> TestAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        var integration = await db.IntegrationConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(connection => connection.Id == integrationId, cancellationToken);
        if (integration is null)
            return IntegrationTestResult.NotFound;
        if (adapters.Find(integration.AdapterType) is not { } adapter)
            return IntegrationTestResult.AdapterUnavailable(AdapterUnavailableError(integration));

        // A disabled integration is tested too: switching one off is how the owner parks it while
        // fixing its settings.
        return await RunTestAsync(
            adapter, integration, integration.BaseUrl, integration.UserName, SecretChange.Keep, SecretChange.Keep, cancellationToken);
    }

    public async Task<IntegrationTestResult> TestDraftAsync(TestIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        IntegrationConnectionEntity? stored = null;
        IIntegrationAdapter adapter;
        if (request.IntegrationId is { } integrationId)
        {
            stored = await db.IntegrationConnections
                .AsNoTracking()
                .SingleOrDefaultAsync(connection => connection.Id == integrationId, cancellationToken);
            // A 404 about an id in the body, not the addressed row, so it says what happened: another
            // tab or person removed the integration while this dialog was open.
            if (stored is null)
                return IntegrationTestResult.EditedIntegrationMissing(EditedIntegrationMissingError);
            // The stored type, whatever the draft says: a type never changes.
            if (adapters.Find(stored.AdapterType) is not { } storedAdapter)
                return IntegrationTestResult.AdapterUnavailable(AdapterUnavailableError(stored));
            adapter = storedAdapter;
        }
        else
        {
            var (requestedAdapter, adapterError) = FindRequestedAdapter(request.AdapterType);
            if (requestedAdapter is null)
                return IntegrationTestResult.Invalid(adapterError!);
            adapter = requestedAdapter;
        }

        // Exactly as a save checks them, less the name and the path mappings, which a test doesn't use:
        // a draft the rules refuse never reaches a service.
        var password = SecretChange.From(request.Password);
        var apiKey = SecretChange.From(request.ApiKey);
        if (ValidateConnection(adapter.Descriptor, request.BaseUrl, request.UserName, password, apiKey, stored) is { } error)
            return IntegrationTestResult.Invalid(error);

        return await RunTestAsync(
            adapter,
            stored,
            IntegrationConnectionRules.NormalizeBaseUrl(request.BaseUrl!),
            NormalizeUserName(request.UserName),
            password,
            apiKey,
            cancellationToken);
    }

    // Works out what the test sends — each secret set, removed or kept — and runs it. A kept secret
    // that can't be decrypted stops the test before any service is called: sending nothing where a key
    // belongs would read as the service refusing it, when the fix is to enter it again (ADR 0007).
    private async Task<IntegrationTestResult> RunTestAsync(
        IIntegrationAdapter adapter,
        IntegrationConnectionEntity? stored,
        string baseUrl,
        string? userName,
        SecretChange password,
        SecretChange apiKey,
        CancellationToken cancellationToken)
    {
        if (!TryResolveSecret(password, stored, IntegrationSecretField.Password, out var passwordValue, out var passwordError))
            return IntegrationTestResult.SecretUnreadable(passwordError);
        if (!TryResolveSecret(apiKey, stored, IntegrationSecretField.ApiKey, out var apiKeyValue, out var apiKeyError))
            return IntegrationTestResult.SecretUnreadable(apiKeyError);

        var settings = new IntegrationConnectionSettings(stored?.Id, baseUrl, userName, passwordValue, apiKeyValue);
        var outcome = await adapter.TestConnectionAsync(settings, cancellationToken);
        // MAM-7 — a second pass after the adapter's own: one line here covers every adapter, a future
        // one that isn't HTTP included, whose message might repeat a secret it was handed.
        var message = SecretRedaction.Redact(outcome.Message, settings.Secrets)!;

        // Information: the owner asked, and the answer is on their screen. Never the address or the
        // message — either may carry what a service said back about a credential.
        logger.LogInformation(
            "Tested integration {IntegrationId} ({AdapterType}): {Status}.", settings.ConnectionId, adapter.Descriptor.TypeId, outcome.Status);
        return IntegrationTestResult.Tested(mapper.ToResponse(outcome with { Message = message }));
    }

    // Set sends the new value, Clear sends none, Keep sends the stored one, opened by the settings
    // factory — the one implementation a library sync uses too (MAM-8). False only for a stored value
    // that can't be decrypted, with the sentence for the owner; the column is never cleared for it.
    private bool TryResolveSecret(
        SecretChange change,
        IntegrationConnectionEntity? stored,
        IntegrationSecretField field,
        out string? secret,
        [NotNullWhen(false)] out string? error)
    {
        secret = null;
        error = null;
        switch (change.Mode)
        {
            case SecretUpsertMode.Set:
                secret = change.NewSecret(field);
                return true;
            case SecretUpsertMode.Clear:
                return true;
        }

        return stored is null || settingsFactory.TryReadSecret(stored, field, out secret, out error);
    }
}
