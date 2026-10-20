using MediaAcquisitionManager.Core.Texts;

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

    private static readonly Message TooShortError = Message.Of("account.password.too-short", ("count", MinimumLength));
    private static readonly Message TooLongError = Message.Of("account.password.too-long", ("count", MaximumLength));

    /// <summary>Why <paramref name="password"/> is refused, as a message the Api renders (ADR 0042), or <c>null</c>
    /// when it is acceptable.</summary>
    public static Message? Validate(string? password) => password?.Length switch
    {
        null or < MinimumLength => TooShortError,
        > MaximumLength => TooLongError,
        _ => null,
    };
}
