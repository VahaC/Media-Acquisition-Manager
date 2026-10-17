namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A new integration, added by the installation owner.</summary>
/// <remarks>
/// MAM-7 — every field nullable and without <c>[Required]</c> on purpose (a non-nullable <c>string</c>
/// would bring MVC's implicit <c>[Required]</c> back): a blank or missing field must reach the
/// integration rules, whose sentence the Add integration dialog shows, not stop at a validation body
/// with no <c>error</c> in it.
/// </remarks>
/// <param name="AdapterType">The type of service (<c>jellyfin</c>, <c>torznab</c>, <c>qbittorrent</c>);
/// see <c>GET api/settings/integrations/adapter-types</c>.</param>
/// <param name="Name">Its display name, unique in any letter case.</param>
/// <param name="BaseUrl">The service's full http:// or https:// address.</param>
/// <param name="UserName">The user name, when the type takes one; blank means none.</param>
/// <param name="Password">The password: Set stores it; Keep, Clear or left out store none.</param>
/// <param name="ApiKey">The API key: Set stores it; Keep, Clear or left out store none.</param>
/// <param name="IsEnabled">Whether the app uses it; left out means on.</param>
/// <param name="PathMappings">How the service's paths map onto this app's, in order; left out means
/// none.</param>
/// <param name="CatalogLanguage">MAM-46 — for a metadata catalog, the language to ask it in: one of its type's
/// <c>catalogLanguages</c>, in any letter case. Left out, the owner's own language for the web app, else the browser's
/// (<c>Accept-Language</c>), else <c>en-US</c>, each brought to the closest language of the list. Refused for any other
/// type.</param>
public sealed record CreateIntegrationRequest(
    string? AdapterType,
    string? Name,
    string? BaseUrl,
    string? UserName = null,
    SecretValueUpsert? Password = null,
    SecretValueUpsert? ApiKey = null,
    bool? IsEnabled = null,
    IReadOnlyList<PathMappingRequest>? PathMappings = null,
    string? CatalogLanguage = null);
