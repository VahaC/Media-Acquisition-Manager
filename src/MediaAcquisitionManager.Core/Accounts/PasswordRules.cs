namespace MediaAcquisitionManager.Core.Accounts;

/// <summary>
/// What a password may be: a length, and nothing else.
/// </summary>
/// <remarks>
/// No composition rules (a digit, a symbol, mixed case), following NIST SP 800-63B: they push people
/// towards "Password1!" and add nothing a longer password doesn't. Every character counts, spaces
/// included — a passphrase is exactly what the length rule invites — so passwords are never trimmed.
/// </remarks>
public static class PasswordRules
{
    /// <summary>NIST SP 800-63B's minimum for a password a user chose.</summary>
    public const int MinimumLength = 8;

    /// <summary>
    /// Bounds what reaches the password hasher, so nobody can make the server run PBKDF2 over a
    /// megabyte per attempt; still far past any passphrase a person types.
    /// </summary>
    public const int MaximumLength = 128;

    private const string TooShortError = "A password must be at least 8 characters long.";
    private const string TooLongError = "A password can be at most 128 characters long.";

    /// <summary>The sentence saying what is wrong with <paramref name="password"/>, or <c>null</c>
    /// when it is acceptable.</summary>
    public static string? Validate(string? password) => password?.Length switch
    {
        null or < MinimumLength => TooShortError,
        > MaximumLength => TooLongError,
        _ => null,
    };
}
