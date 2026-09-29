namespace MediaAcquisitionManager.Core.Integrations;

/// <summary>
/// What a new integration secret may be — a value being set, never one already stored (MAM-7). Each
/// rule answers one readable sentence, or <c>null</c> when the value is acceptable.
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

    private const string ApiKeyBlankError = "A new API key can't be blank.";
    private const string ApiKeyTooShortError = "An API key must be at least 8 characters long.";
    private const string ApiKeyTooLongError = "An API key can be at most 256 characters long.";
    private const string ApiKeyCharactersError = "An API key can contain only visible characters, without spaces, quotes or backslashes.";
    private const string PasswordBlankError = "A new password can't be blank.";
    private const string PasswordTooLongError = "A password can be at most 256 characters long.";
    private const string PasswordControlCharacterError = "A password can't contain line breaks or other control characters.";

    /// <summary>
    /// The sentence saying what is wrong with <paramref name="apiKey"/>, or <c>null</c>. Surrounding
    /// whitespace is ignored — a key is pasted, and a pasted line break is never part of it — and
    /// whoever stores the key stores it trimmed.
    /// </summary>
    public static string? ValidateNewApiKey(string? apiKey)
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
    /// The sentence saying what is wrong with <paramref name="password"/>, or <c>null</c>. Never
    /// trimmed: every character of a password counts, spaces included (the
    /// <see cref="Accounts.PasswordRules"/> stance).
    /// </summary>
    public static string? ValidateNewPassword(string? password)
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
