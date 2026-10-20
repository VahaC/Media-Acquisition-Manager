using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Integrations;

/// <summary>
/// What an integration's name, address, user name and credentials may be (MAM-7). Each rule answers
/// why a value is refused, as a message the Api renders (ADR 0042), or <c>null</c> when the value is acceptable — the
/// MAM-4 rules-class pattern.
/// </summary>
/// <remarks>
/// <para>
/// No private-address (SSRF) guard on the address: these services live on the owner's LAN by design
/// (192.168.1.195 in the reference installation), only the installation owner can enter one, and
/// requests to it follow no redirects. Specification §4's SSRF concern is about addresses an indexer
/// supplies: MAM-10's payload fetch only ever requests the feed's own origin and follows no redirect,
/// so an indexer's address is never requested (ADR 0007).
/// </para>
/// <para>
/// <c>{{service}}</c> in a message is the adapter type's display name, a product name the owner knows. MAM-47 — a key
/// per credential and verb rather than "a user name" filled into one frame: in Ukrainian the noun's case follows the
/// verb.
/// </para>
/// </remarks>
public static class IntegrationConnectionRules
{
    /// <summary>Fits a list row and a provenance line on a phone; far past any name worth typing.</summary>
    public const int NameMaximumLength = 64;

    /// <summary>Browsers' practical URL limit; a base address is a fraction of it.</summary>
    public const int BaseUrlMaximumLength = 2048;

    /// <summary>Longer than any user name a service accepts, short enough to bound what is stored and
    /// sent.</summary>
    public const int UserNameMaximumLength = 256;

    private static readonly Message NameMissingError = Message.Of("integrations.connection.name-missing");
    private static readonly Message NameTooLongError =
        Message.Of("integrations.connection.name-too-long", ("count", NameMaximumLength));
    private static readonly Message NameControlCharacterError = Message.Of("integrations.connection.name-control-characters");
    private static readonly Message BaseUrlMissingError = Message.Of("integrations.connection.address-missing");
    private static readonly Message BaseUrlTooLongError =
        Message.Of("integrations.connection.address-too-long", ("count", BaseUrlMaximumLength));
    private static readonly Message BaseUrlNotHttpError = Message.Of("integrations.connection.address-not-http");
    private static readonly Message BaseUrlUserInfoError = Message.Of("integrations.connection.address-user-info");
    private static readonly Message BaseUrlQueryError = Message.Of("integrations.connection.address-query");
    private static readonly Message BaseUrlFragmentError = Message.Of("integrations.connection.address-fragment");
    private static readonly Message UserNameTooLongError =
        Message.Of("integrations.connection.user-name-too-long", ("count", UserNameMaximumLength));
    private static readonly Message UserNameControlCharacterError = Message.Of("integrations.connection.user-name-control-characters");
    private static readonly Message PasswordWithoutUserNameError = Message.Of("integrations.connection.password-without-user-name");

    /// <summary>The form names are compared in: trimmed and upper-cased, culture independent, so
    /// "Jellyfin" and "jellyfin " are one name.</summary>
    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    /// <summary>Why <paramref name="name"/> is refused, or <c>null</c>.
    /// Surrounding whitespace is ignored; whoever stores the name stores it trimmed.</summary>
    public static Message? ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            return NameMissingError;
        if (trimmed.Length > NameMaximumLength)
            return NameTooLongError;
        // A line break in a name splits a log line and an audit row in two.
        if (trimmed.Any(char.IsControl))
            return NameControlCharacterError;
        return null;
    }

    /// <summary>
    /// Why <paramref name="baseUrl"/> is refused, or <c>null</c>. No message
    /// repeats the address: a pasted <c>?apikey=…</c> must not bounce back into a response or a log.
    /// </summary>
    public static Message? ValidateBaseUrl(string? baseUrl)
    {
        var trimmed = baseUrl?.Trim() ?? "";
        if (trimmed.Length == 0)
            return BaseUrlMissingError;
        if (trimmed.Length > BaseUrlMaximumLength)
            return BaseUrlTooLongError;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return BaseUrlNotHttpError;
        // A credential in the address would be stored in plain text, returned by every read and sent
        // in every log line that names the address; it has an encrypted field of its own.
        if (uri.UserInfo.Length > 0)
            return BaseUrlUserInfoError;
        if (uri.Query.Length > 0)
            return BaseUrlQueryError;
        if (uri.Fragment.Length > 0)
            return BaseUrlFragmentError;
        return null;
    }

    /// <summary>
    /// The form an address is stored in, once <see cref="ValidateBaseUrl"/> accepted it: scheme and
    /// host lower-cased, a default port dropped, the path kept, no trailing slash — so an adapter
    /// appends its own path with one <c>/</c>.
    /// </summary>
    /// <remarks>
    /// Scheme, server and path components rather than <see cref="Uri.GetLeftPart"/>, which would keep
    /// the stray <c>@</c> of an empty user info (<c>http://@host</c>).
    /// </remarks>
    public static string NormalizeBaseUrl(string baseUrl) =>
        new Uri(baseUrl.Trim())
            .GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)
            .TrimEnd('/');

    /// <summary>
    /// Why <paramref name="userName"/> is refused for <paramref name="adapter"/>,
    /// or <c>null</c>. A blank one is absent, not wrong: whether it is needed is
    /// <see cref="ValidateCredentials"/>'s question.
    /// </summary>
    public static Message? ValidateUserName(string? userName, IntegrationAdapterDescriptor adapter)
    {
        var trimmed = userName?.Trim() ?? "";
        if (trimmed.Length == 0)
            return null;
        if (adapter.UserName == CredentialRequirement.NotUsed)
            return Service("integrations.connection.user-name-not-used", adapter);
        if (trimmed.Length > UserNameMaximumLength)
            return UserNameTooLongError;
        if (trimmed.Any(char.IsControl))
            return UserNameControlCharacterError;
        return null;
    }

    /// <summary>
    /// Which credential <paramref name="adapter"/> is missing or doesn't use, as a message the Api renders, or
    /// <c>null</c>. Checked on the <b>effective</b> state, after the secret changes of a request are
    /// applied: Set → present, Clear → absent, Keep → whether the stored column is non-null.
    /// </summary>
    public static Message? ValidateCredentials(IntegrationAdapterDescriptor adapter, bool hasUserName, bool hasPassword, bool hasApiKey)
    {
        if (adapter.UserName == CredentialRequirement.NotUsed && hasUserName)
            return Service("integrations.connection.user-name-not-used", adapter);
        if (adapter.Password == CredentialRequirement.NotUsed && hasPassword)
            return Service("integrations.connection.password-not-used", adapter);
        if (adapter.ApiKey == CredentialRequirement.NotUsed && hasApiKey)
            return Service("integrations.connection.api-key-not-used", adapter);

        if (adapter.UserName == CredentialRequirement.Required && !hasUserName)
            return Service("integrations.connection.user-name-needed", adapter);
        if (adapter.Password == CredentialRequirement.Required && !hasPassword)
            return Service("integrations.connection.password-needed", adapter);
        if (adapter.ApiKey == CredentialRequirement.Required && !hasApiKey)
            return Service("integrations.connection.api-key-needed", adapter);

        // Both optional (qBittorrent can let a subnet in without signing in): the service takes both
        // or neither, and half a pair would only fail at the first test with a vaguer sentence.
        if (adapter.UserName == CredentialRequirement.Optional && adapter.Password == CredentialRequirement.Optional)
        {
            if (hasPassword && !hasUserName)
                return PasswordWithoutUserNameError;
            if (hasUserName && !hasPassword)
                return Service("integrations.connection.user-name-without-password", adapter);
        }

        return null;
    }

    // The message whose one value is the adapter type's display name.
    private static Message Service(string key, IntegrationAdapterDescriptor adapter) => Message.Of(key, ("service", adapter.DisplayName));
}
