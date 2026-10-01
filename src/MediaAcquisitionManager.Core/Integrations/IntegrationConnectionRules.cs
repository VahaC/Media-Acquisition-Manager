using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Integrations;

/// <summary>
/// What an integration's name, address, user name and credentials may be (MAM-7). Each rule answers
/// one readable sentence, or <c>null</c> when the value is acceptable — the MAM-4 rules-class pattern.
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
/// <c>{Display}</c> in a sentence is the adapter type's display name, a product name the owner knows.
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

    private const string NameMissingError = "An integration needs a name.";
    private const string NameTooLongError = "An integration name can be at most 64 characters long.";
    private const string NameControlCharacterError = "An integration name can't contain line breaks or other control characters.";
    private const string BaseUrlMissingError = "An integration needs the address of the service, such as http://192.168.1.10:8096.";
    private const string BaseUrlTooLongError = "The address can be at most 2048 characters long.";
    private const string BaseUrlNotHttpError = "The address must be a full http:// or https:// address, such as http://192.168.1.10:8096.";
    private const string BaseUrlUserInfoError = "Leave the user name and password out of the address; they have fields of their own.";
    private const string BaseUrlQueryError = "The address can't have a query (the part from ?). An API key has a field of its own.";
    private const string BaseUrlFragmentError = "The address can't have a fragment (the part from #).";
    private const string UserNameTooLongError = "A user name can be at most 256 characters long.";
    private const string UserNameControlCharacterError = "A user name can't contain line breaks or other control characters.";
    private const string PasswordWithoutUserNameError = "Enter the user name that goes with the password.";

    /// <summary>The form names are compared in: trimmed and upper-cased, culture independent, so
    /// "Jellyfin" and "jellyfin " are one name.</summary>
    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    /// <summary>The sentence saying what is wrong with <paramref name="name"/>, or <c>null</c>.
    /// Surrounding whitespace is ignored; whoever stores the name stores it trimmed.</summary>
    public static string? ValidateName(string? name)
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
    /// The sentence saying what is wrong with <paramref name="baseUrl"/>, or <c>null</c>. No sentence
    /// repeats the address: a pasted <c>?apikey=…</c> must not bounce back into a response or a log.
    /// </summary>
    public static string? ValidateBaseUrl(string? baseUrl)
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
    /// The sentence saying what is wrong with <paramref name="userName"/> for <paramref name="adapter"/>,
    /// or <c>null</c>. A blank one is absent, not wrong: whether it is needed is
    /// <see cref="ValidateCredentials"/>'s question.
    /// </summary>
    public static string? ValidateUserName(string? userName, IntegrationAdapterDescriptor adapter)
    {
        var trimmed = userName?.Trim() ?? "";
        if (trimmed.Length == 0)
            return null;
        if (adapter.UserName == CredentialRequirement.NotUsed)
            return NotUsedError(adapter, "a user name");
        if (trimmed.Length > UserNameMaximumLength)
            return UserNameTooLongError;
        if (trimmed.Any(char.IsControl))
            return UserNameControlCharacterError;
        return null;
    }

    /// <summary>
    /// The sentence saying which credential <paramref name="adapter"/> is missing or doesn't use, or
    /// <c>null</c>. Checked on the <b>effective</b> state, after the secret changes of a request are
    /// applied: Set → present, Clear → absent, Keep → whether the stored column is non-null.
    /// </summary>
    public static string? ValidateCredentials(IntegrationAdapterDescriptor adapter, bool hasUserName, bool hasPassword, bool hasApiKey)
    {
        if (adapter.UserName == CredentialRequirement.NotUsed && hasUserName)
            return NotUsedError(adapter, "a user name");
        if (adapter.Password == CredentialRequirement.NotUsed && hasPassword)
            return NotUsedError(adapter, "a password");
        if (adapter.ApiKey == CredentialRequirement.NotUsed && hasApiKey)
            return NotUsedError(adapter, "an API key");

        if (adapter.UserName == CredentialRequirement.Required && !hasUserName)
            return NeededError(adapter, "a user name");
        if (adapter.Password == CredentialRequirement.Required && !hasPassword)
            return NeededError(adapter, "a password");
        if (adapter.ApiKey == CredentialRequirement.Required && !hasApiKey)
            return NeededError(adapter, "an API key");

        // Both optional (qBittorrent can let a subnet in without signing in): the service takes both
        // or neither, and half a pair would only fail at the first test with a vaguer sentence.
        if (adapter.UserName == CredentialRequirement.Optional && adapter.Password == CredentialRequirement.Optional)
        {
            if (hasPassword && !hasUserName)
                return PasswordWithoutUserNameError;
            if (hasUserName && !hasPassword)
                return $"Enter the password that goes with the user name, or leave both empty if {adapter.DisplayName} lets this app in without signing in.";
        }

        return null;
    }

    private static string NotUsedError(IntegrationAdapterDescriptor adapter, string credential) =>
        $"{adapter.DisplayName} doesn't use {credential}.";

    private static string NeededError(IntegrationAdapterDescriptor adapter, string credential) =>
        $"{adapter.DisplayName} needs {credential}.";
}
