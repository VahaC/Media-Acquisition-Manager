using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A type of service this build can connect to, and what an integration of it takes.</summary>
/// <param name="TypeId">The id an integration of the type is created with.</param>
/// <param name="DisplayName">The product name; shown as is, never translated.</param>
/// <param name="Kind">What the app uses the type for.</param>
/// <param name="UserName">Whether it takes a user name.</param>
/// <param name="Password">Whether it takes a password.</param>
/// <param name="ApiKey">Whether it takes an API key.</param>
/// <param name="ReportsFilePaths">Whether the service reports file paths, so path mappings apply.</param>
/// <param name="DestinationKind">MAM-30 — for a type of download client, what its destinations are: its own categories
/// or the folders it can reach (ADR 0039); <c>null</c> for any other type.</param>
public sealed record IntegrationAdapterTypeDto(
    string TypeId,
    string DisplayName,
    IntegrationKind Kind,
    CredentialRequirement UserName,
    CredentialRequirement Password,
    CredentialRequirement ApiKey,
    bool ReportsFilePaths,
    DownloadDestinationKind? DestinationKind = null);
