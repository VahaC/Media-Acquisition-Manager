using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IIntegrationConnectionSeeder"/>
public sealed class IntegrationConnectionSeeder(IServiceProvider services) : SeederBase(services), IIntegrationConnectionSeeder
{
    /// <summary>
    /// A valid encryption key (32 bytes, base64) that is not the host's: what it seals, the host can't
    /// read.
    /// </summary>
    public const string OtherEncryptionKey = "8XRs8I1z3mNxYJLt4pKY0zvgabjO9q1iy6B5wZHS5bA=";

    /// <summary>The API key <see cref="CreateWithUnreadableApiKey"/> seals under the other key.</summary>
    public const string UnreadableApiKey = "sealed-under-another-key";

    /// <summary>
    /// MAM-46 — the language a seeded metadata catalog is asked in unless a test names one: Ukrainian, the language every
    /// catalog call asked in before MAM-46 and the one an upgraded installation's TMDb keeps, so the tests written before
    /// it see the same requests. The catalog rows the data factories plant are stamped with it too.
    /// </summary>
    public const string CatalogLanguage = "uk-UA";

    private IIntegrationConnectionDataFactory Integrations =>
        new IntegrationConnectionDataFactory(Db, Resolve<IEncryptionService>(), Resolve<TimeProvider>());

    public Task<IntegrationConnectionEntity> Create(
        string adapterType = FakeIntegrationAdapter.TypeId,
        string? name = null,
        string baseUrl = "http://127.0.0.1:9",
        string? userName = null,
        string? password = null,
        string? apiKey = null,
        bool isEnabled = true,
        IReadOnlyList<PathMappingPair>? pathMappings = null,
        IntegrationKind? kind = null,
        string? catalogLanguage = null) =>
        Integrations.Create(
            name ?? NewName(),
            adapterType,
            KindOf(adapterType, kind),
            baseUrl,
            userName,
            password,
            apiKey,
            isEnabled,
            pathMappings,
            catalogLanguage ?? CatalogLanguageOf(adapterType));

    public async Task<IntegrationConnectionEntity> CreateWithUnreadableApiKey(string adapterType = "jellyfin", string? name = null)
    {
        var integration = await Create(adapterType, name);
        // The real service under another key, with the right purpose: only the key differs, so the
        // host's refusal can only be the key's doing.
        var otherKey = new AesGcmEncryptionService(Options.Create(new EncryptionOptions { Key = OtherEncryptionKey }));
        integration.ApiKeyEncrypted = otherKey.Encrypt(
            UnreadableApiKey, IntegrationSecretPurpose.For(integration.Id, IntegrationSecretField.ApiKey));
        await Integrations.SetApiKeyCiphertext(integration.Id, integration.ApiKeyEncrypted);
        return integration;
    }

    public async Task<IntegrationConnectionEntity> CreateWithCopiedApiKey(Guid fromIntegrationId)
    {
        var source = await Db.IntegrationConnections.AsNoTracking().SingleAsync(x => x.Id == fromIntegrationId);
        var integration = await Create(source.AdapterType, baseUrl: source.BaseUrl, kind: source.Kind);
        // Verbatim: sealed under the host's own key, but for the other row.
        integration.ApiKeyEncrypted = source.ApiKeyEncrypted;
        await Integrations.SetApiKeyCiphertext(integration.Id, integration.ApiKeyEncrypted);
        return integration;
    }

    // MAM-46 — as the API would store it: a language for a metadata catalog only.
    private string? CatalogLanguageOf(string adapterType) =>
        Resolve<IIntegrationAdapterRegistry>().Find<IMetadataCatalog>(adapterType) is null ? null : CatalogLanguage;

    // As the API would store it: the kind of the registered type, whatever the caller says.
    private IntegrationKind KindOf(string adapterType, IntegrationKind? kind) =>
        Resolve<IIntegrationAdapterRegistry>().Find(adapterType)?.Descriptor.Kind ?? kind ?? IntegrationKind.MediaLibrary;

    // Unique, and within the name rules (at most 64 characters).
    private static string NewName() => $"integration-{Guid.NewGuid():N}";
}
