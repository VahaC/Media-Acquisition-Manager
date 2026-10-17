namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The new settings of an integration, saved by the edit dialog's one Save.</summary>
/// <remarks>
/// <para>
/// MAM-7 — every field nullable and without <c>[Required]</c> on purpose (a non-nullable <c>string</c>
/// would bring MVC's implicit <c>[Required]</c> back): a blank or missing field must reach the
/// integration rules, whose sentence the dialog shows, not stop at a validation body with no
/// <c>error</c> in it.
/// </para>
/// <para>
/// No adapter type: a type never changes, and one sent anyway is ignored. <paramref name="IsEnabled"/>,
/// <paramref name="PathMappings"/> and — MAM-46 — <paramref name="CatalogLanguage"/> left out keep what is stored
/// rather than resetting it, so a client that doesn't know a field can't switch an integration back on, delete its
/// mappings or change the language its catalog is read in.
/// </para>
/// </remarks>
/// <param name="Name">Its display name, unique in any letter case.</param>
/// <param name="BaseUrl">The service's full http:// or https:// address.</param>
/// <param name="UserName">The user name; <c>null</c> or blank means none.</param>
/// <param name="Password">Set replaces the password, Clear removes it, Keep or left out keeps it.</param>
/// <param name="ApiKey">Set replaces the API key, Clear removes it, Keep or left out keeps it.</param>
/// <param name="IsEnabled">Whether the app uses it; left out keeps the switch as it is.</param>
/// <param name="PathMappings">Every path mapping, in order, replacing the stored ones; <c>[]</c> removes
/// them all, left out keeps them.</param>
/// <param name="CatalogLanguage">MAM-46 — for a metadata catalog, the language to ask it in from now on: one of its
/// type's <c>catalogLanguages</c>, in any letter case; left out keeps it. A change reads the catalog again in it at the
/// next computation. Refused for any other type.</param>
public sealed record UpdateIntegrationRequest(
    string? Name,
    string? BaseUrl,
    string? UserName = null,
    SecretValueUpsert? Password = null,
    SecretValueUpsert? ApiKey = null,
    bool? IsEnabled = null,
    IReadOnlyList<PathMappingRequest>? PathMappings = null,
    string? CatalogLanguage = null);
