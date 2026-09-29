using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IIntegrationConnectionDataFactory"/>
public sealed class IntegrationConnectionDataFactory(ApplicationDbContext db, IEncryptionService encryption, TimeProvider time)
    : IIntegrationConnectionDataFactory
{
    public async Task<IntegrationConnectionEntity> Create(
        string name,
        string adapterType,
        IntegrationKind kind,
        string baseUrl = "http://127.0.0.1:9",
        string? userName = null,
        string? password = null,
        string? apiKey = null,
        bool isEnabled = true,
        IReadOnlyList<PathMappingPair>? pathMappings = null)
    {
        var integration = new IntegrationConnectionEntity
        {
            Name = name,
            NormalizedName = IntegrationConnectionRules.NormalizeName(name),
            AdapterType = adapterType,
            Kind = kind,
            IsEnabled = isEnabled,
            BaseUrl = baseUrl,
            UserName = userName,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        // Sealed with the row's own id, which exists from construction: a test that decrypts through
        // the host's service with the app's purpose proves the app would read it back.
        integration.PasswordEncrypted = Seal(password, integration.Id, IntegrationSecretField.Password);
        integration.ApiKeyEncrypted = Seal(apiKey, integration.Id, IntegrationSecretField.ApiKey);
        integration.PathMappings = [.. (pathMappings ?? []).Select((pair, position) => new PathMappingEntity
        {
            IntegrationConnectionId = integration.Id,
            Position = position,
            ServicePrefix = pair.ServicePrefix,
            AppPrefix = pair.AppPrefix,
        })];

        db.IntegrationConnections.Add(integration);
        await db.SaveChangesAsync();
        // Detached, so the test's later reads come from the database, not from this tracked copy.
        db.ChangeTracker.Clear();
        return integration;
    }

    // Straight to the column, bypassing the tracker: the point is a value the app never wrote.
    public Task SetApiKeyCiphertext(Guid id, string? ciphertext) =>
        db.IntegrationConnections
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ApiKeyEncrypted, ciphertext));

    private string? Seal(string? secret, Guid integrationId, IntegrationSecretField field) =>
        secret is null ? null : encryption.Encrypt(secret, IntegrationSecretPurpose.For(integrationId, field));
}
