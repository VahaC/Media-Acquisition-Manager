namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>Whether the settings of a saved integration could be built.</summary>
public enum IntegrationSettingsStatus
{
    /// <summary>Built, every stored secret decrypted.</summary>
    Built = 1,

    /// <summary>A stored secret can't be decrypted with this installation's key; the error says which
    /// and what to do.</summary>
    SecretUnreadable = 2,
}
