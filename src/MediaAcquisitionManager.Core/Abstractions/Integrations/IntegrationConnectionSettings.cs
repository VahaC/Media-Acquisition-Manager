namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>
/// Everything one call to an integration needs, secrets decrypted. Built for the call and never
/// stored.
/// </summary>
/// <param name="ConnectionId">The saved integration; <c>null</c> for an unsaved draft.</param>
/// <param name="BaseUrl">The normalised address: absolute http(s), no user info, query, fragment or
/// trailing slash.</param>
/// <param name="UserName">The user name, or <c>null</c>.</param>
/// <param name="Password">The password in plain text, or <c>null</c>.</param>
/// <param name="ApiKey">The API key in plain text, or <c>null</c>.</param>
/// <param name="CatalogLanguage">MAM-46 — for a metadata catalog, the language its answers are asked in (a tag of its
/// own list, such as <c>uk-UA</c>); <c>null</c> for any other integration and for a draft, whose catalog then asks in
/// its own default. Trailing and optional, so every call that needs no language builds the settings as before.</param>
public sealed record IntegrationConnectionSettings(
    Guid? ConnectionId,
    string BaseUrl,
    string? UserName,
    string? Password,
    string? ApiKey,
    string? CatalogLanguage = null)
{
    /// <summary>Every secret of this call, for <see cref="SecretRedaction.Redact"/>.</summary>
    public string?[] Secrets => [Password, ApiKey];

    /// <summary><see cref="BaseUrl"/> joined with <paramref name="relativePath"/>, which may carry a
    /// query. The address's own path is kept (<c>http://h/jellyfin</c> + <c>System/Info</c>).</summary>
    public Uri Resolve(string relativePath) => new($"{BaseUrl}/{relativePath.TrimStart('/')}");

    /// <summary>The settings with both secrets masked.</summary>
    /// <remarks>
    /// MAM-7 — a record's generated <c>ToString</c> prints every property: one
    /// <c>logger.LogWarning("{Settings}", settings)</c> would ship the password and the key off the
    /// box with every Warning.
    /// </remarks>
    public override string ToString() =>
        $"IntegrationConnectionSettings {{ ConnectionId = {ConnectionId}, BaseUrl = {BaseUrl}, UserName = {UserName}, " +
        $"Password = {(Password is null ? "null" : SecretRedaction.Mask)}, ApiKey = {(ApiKey is null ? "null" : SecretRedaction.Mask)}, " +
        $"CatalogLanguage = {CatalogLanguage} }}";
}
