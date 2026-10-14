using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>An integration as the Integrations settings page shows it.</summary>
/// <remarks>
/// MAM-7 — secrets never leave (ADR 0007): no property holds a password, an API key or what either is
/// sealed as, only whether one is stored.
/// </remarks>
/// <param name="Id">The integration's id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="AdapterType">The type id it was created with (<c>jellyfin</c>, <c>torznab</c>,
/// <c>qbittorrent</c>); it never changes.</param>
/// <param name="AdapterDisplayName">The type's product name; <c>null</c> when this version of the app
/// no longer connects to the type, so the integration can only be removed.</param>
/// <param name="Kind">What the app uses it for.</param>
/// <param name="IsEnabled">Whether the app uses it; a disabled one keeps its settings.</param>
/// <param name="BaseUrl">The service's address, normalised; it can't hold a secret.</param>
/// <param name="UserName">The user name, or <c>null</c>. Not a secret.</param>
/// <param name="HasPassword">Whether a password is stored.</param>
/// <param name="HasApiKey">Whether an API key is stored.</param>
/// <param name="PathMappings">Its path mappings, in the order they were entered.</param>
/// <param name="CreatedUtc">When it was added.</param>
/// <param name="DestinationKind">MAM-30 — for a download client, what its destinations are: its own categories or the
/// folders it can reach (ADR 0039); <c>null</c> for any other integration, and for one whose type this version of the
/// app no longer connects to.</param>
public sealed record IntegrationDto(
    Guid Id,
    string Name,
    string AdapterType,
    string? AdapterDisplayName,
    IntegrationKind Kind,
    bool IsEnabled,
    string BaseUrl,
    string? UserName,
    bool HasPassword,
    bool HasApiKey,
    IReadOnlyList<PathMappingDto> PathMappings,
    DateTime CreatedUtc,
    DownloadDestinationKind? DestinationKind = null);
