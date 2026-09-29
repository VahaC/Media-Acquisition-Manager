using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates or changes integration rows directly in the database.</summary>
public interface IIntegrationConnectionDataFactory
{
    /// <summary>
    /// A saved integration with its path mappings, each secret sealed for this row and field exactly
    /// as the app seals it (<c>IntegrationSecretPurpose</c>). Values are stored as given — the address
    /// and the mapping paths too — so a test can plant a row the API would have refused.
    /// </summary>
    Task<IntegrationConnectionEntity> Create(
        string name,
        string adapterType,
        IntegrationKind kind,
        string baseUrl = "http://127.0.0.1:9",
        string? userName = null,
        string? password = null,
        string? apiKey = null,
        bool isEnabled = true,
        IReadOnlyList<PathMappingPair>? pathMappings = null);

    /// <summary>Overwrites the stored API key column with <paramref name="ciphertext"/> as is:
    /// a value sealed under another key or copied from another row, or <c>null</c>.</summary>
    Task SetApiKeyCiphertext(Guid id, string? ciphertext);
}
