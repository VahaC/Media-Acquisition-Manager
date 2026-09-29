namespace MediaAcquisitionManager.Core.Integrations;

/// <summary>
/// The associated data an integration's secret is sealed with (ADR 0007): the row and the column it
/// belongs to, so a sealed value copied to another row or into the other column fails to open instead
/// of being sent to that row's server.
/// </summary>
/// <remarks>
/// Row ids come from <see cref="Entities.BaseEntity"/> at construction, so the purpose is known before
/// the row's first save.
/// </remarks>
public static class IntegrationSecretPurpose
{
    /// <summary>
    /// <c>integration-connection/{connectionId:N}/password</c> or <c>…/api-key</c>. Literal field
    /// names rather than the enum's <c>ToString</c>: changing this text makes every stored secret
    /// unreadable, which is why a test pins it.
    /// </summary>
    public static string For(Guid connectionId, IntegrationSecretField field) => field switch
    {
        IntegrationSecretField.Password => $"integration-connection/{connectionId:N}/password",
        IntegrationSecretField.ApiKey => $"integration-connection/{connectionId:N}/api-key",
        // A caller's bug, not input: sealing under a made-up purpose would store a value nothing opens.
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a secret field of an integration."),
    };
}
