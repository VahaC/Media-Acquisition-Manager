namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>Whether an adapter type takes one kind of credential.</summary>
public enum CredentialRequirement
{
    /// <summary>The type has no use for it; one given is refused rather than stored unused.</summary>
    NotUsed = 1,

    /// <summary>The service may or may not ask for it (qBittorrent can let a subnet in without
    /// signing in).</summary>
    Optional = 2,

    /// <summary>The service can't be used without it.</summary>
    Required = 3,
}
