namespace MediaAcquisitionManager.Core.Integrations;

/// <summary>Which secret column of an integration a value belongs to.</summary>
/// <remarks>Never stored by name or number: <see cref="IntegrationSecretPurpose.For"/> maps each member to
/// a literal text, so renaming a member can't orphan the secrets sealed for it.</remarks>
public enum IntegrationSecretField
{
    /// <summary>The password column.</summary>
    Password = 1,

    /// <summary>The API key column.</summary>
    ApiKey = 2,
}
