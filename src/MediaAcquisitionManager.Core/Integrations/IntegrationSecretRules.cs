using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Integrations;

/// <summary>
/// What a new integration secret may be — a value being set, never one already stored (MAM-7). Each
/// rule answers why a value is refused, as a message the Api renders (ADR 0042), or <c>null</c> when the value is
/// acceptable.
/// </summary>
public static class IntegrationSecretRules
{
    /// <summary>Far past any key or password a service issues; bounds what is sealed and stored in a
    /// column of 2048.</summary>
    public const int MaximumLength = 256;

    /// <summary>
    /// <see cref="Abstractions.SecretRedaction"/> masks only values of 8 or more characters, so a
    /// shorter key couldn't be masked if a service echoed it back in an error.
    /// </summary>
    public const int ApiKeyMinimumLength = 8;

    private static readonly Message ApiKeyBlankError = Message.Of("integrations.secret.api-key-blank");
    private static readonly Message ApiKeyTooShortError =
        Message.Of("integrations.secret.api-key-too-short", ("count", ApiKeyMinimumLength));
    private static readonly Message ApiKeyTooLongError = Message.Of("integrations.secret.api-key-too-long", ("count", MaximumLength));
    private static readonly Message ApiKeyCharactersError = Message.Of("integrations.secret.api-key-characters");
    private static readonly Message PasswordBlankError = Message.Of("integrations.secret.password-blank");

    // MAM-47 — the account password's words with this rule's bound: one wording is one key (the converter's round trip).
    private static readonly Message PasswordTooLongError = Message.Of("account.password.too-long", ("count", MaximumLength));
    private static readonly Message PasswordControlCharacterError = Message.Of("integrations.secret.password-control-characters");

    /// <summary>
    /// Why <paramref name="apiKey"/> is refused, or <c>null</c>. Surrounding
    /// whitespace is ignored — a key is pasted, and a pasted line break is never part of it — and
    /// whoever stores the key stores it trimmed.
    /// </summary>
    public static Message? ValidateNewApiKey(string? apiKey)
    {
        var trimmed = apiKey?.Trim() ?? "";
        if (trimmed.Length == 0)
            return ApiKeyBlankError;
        if (trimmed.Length < ApiKeyMinimumLength)
            return ApiKeyTooShortError;
        if (trimmed.Length > MaximumLength)
            return ApiKeyTooLongError;
        // Visible ASCII only, and no quote or backslash: a key travels in a header
        // (MediaBrowser Token="…") and a query string, where either would break the syntax.
        if (trimmed.Any(character => character is < '!' or > '~' or '"' or '\\'))
            return ApiKeyCharactersError;
        return null;
    }

    /// <summary>
    /// Why <paramref name="password"/> is refused, or <c>null</c>. Never
    /// trimmed: every character of a password counts, spaces included (the
    /// <see cref="Accounts.PasswordRules"/> stance).
    /// </summary>
    public static Message? ValidateNewPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return PasswordBlankError;
        if (password.Length > MaximumLength)
            return PasswordTooLongError;
        // A line break would end the form field it is posted in, or split a header.
        if (password.Any(char.IsControl))
            return PasswordControlCharacterError;
        return null;
    }
}
