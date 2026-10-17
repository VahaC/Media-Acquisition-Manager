using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>
/// Integrations for a test, saved straight to the database with their secrets sealed the way the app
/// seals them. A <c>null</c> name makes a unique one; the address defaults to one where nothing
/// listens (<c>http://127.0.0.1:9</c>), never a real host.
/// </summary>
public interface IIntegrationConnectionSeeder
{
    /// <summary>
    /// A saved integration of <paramref name="adapterType"/>. Its kind is the registered type's;
    /// for a type this build doesn't register, <paramref name="kind"/> or else a media library. Values
    /// are stored as given, so a test can plant a row the API would refuse. MAM-46 — a metadata catalog's language is
    /// <paramref name="catalogLanguage"/>, or <see cref="IntegrationConnectionSeeder.CatalogLanguage"/> when it is
    /// <c>null</c>; any other type has none.
    /// </summary>
    Task<IntegrationConnectionEntity> Create(
        string adapterType = FakeIntegrationAdapter.TypeId,
        string? name = null,
        string baseUrl = "http://127.0.0.1:9",
        string? userName = null,
        string? password = null,
        string? apiKey = null,
        bool isEnabled = true,
        IReadOnlyList<PathMappingPair>? pathMappings = null,
        IntegrationKind? kind = null,
        string? catalogLanguage = null);

    /// <summary>
    /// A saved integration whose API key was sealed, for this row and field, under another encryption
    /// key than the host's — what a changed <c>MAM_Encryption__Key</c> leaves behind. The entity carries
    /// the stored value.
    /// </summary>
    Task<IntegrationConnectionEntity> CreateWithUnreadableApiKey(string adapterType = "jellyfin", string? name = null);

    /// <summary>
    /// A saved integration of the same type and address as <paramref name="fromIntegrationId"/>, whose
    /// API key column holds that integration's sealed value copied verbatim — a hand-restored or
    /// tampered database. The entity carries the stored value.
    /// </summary>
    Task<IntegrationConnectionEntity> CreateWithCopiedApiKey(Guid fromIntegrationId);
}
