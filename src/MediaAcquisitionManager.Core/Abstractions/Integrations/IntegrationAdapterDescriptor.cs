using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>What an adapter type is, what the core uses it for and which credentials it takes.</summary>
/// <param name="TypeId">
/// The stable id stored on connection rows, matching <c>^[a-z][a-z0-9-]{1,31}$</c>. Part of the
/// on-disk contract: renaming it orphans every stored connection of the type (ADR 0005).
/// </param>
/// <param name="DisplayName">The product name shown in the UI and in the API's sentences; not
/// translated, because it is a name.</param>
/// <param name="Kind">What the core uses the type for.</param>
/// <param name="UserName">Whether the type takes a user name.</param>
/// <param name="Password">Whether the type takes a password.</param>
/// <param name="ApiKey">Whether the type takes an API key.</param>
/// <param name="ReportsFilePaths">
/// Whether the service reports file paths, so path mappings apply to it (ADR 0003). False for a
/// search source: the app never trusts an indexer's paths (specification §4).
/// </param>
public sealed record IntegrationAdapterDescriptor(
    string TypeId,
    string DisplayName,
    IntegrationKind Kind,
    CredentialRequirement UserName,
    CredentialRequirement Password,
    CredentialRequirement ApiKey,
    bool ReportsFilePaths);
