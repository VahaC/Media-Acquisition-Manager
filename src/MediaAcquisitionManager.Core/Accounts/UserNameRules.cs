using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Accounts;

/// <summary>
/// What a user name may be, and the one form names are compared in.
/// </summary>
/// <remarks>
/// ASCII letters, digits, dots, hyphens and underscores only: a name is typed on every sign-in, from
/// any keyboard, and appears in logs and audit rows — no look-alike Unicode letters that make two
/// different accounts read the same, no spaces that vanish at the end of a field. Surrounding
/// whitespace is not part of a name: both <see cref="Validate"/> and <see cref="Normalize"/> ignore
/// it, and whoever stores a name stores it trimmed.
/// </remarks>
public static class UserNameRules
{
    /// <summary>Short enough to fit a menu button on a phone; long enough for "firstname.lastname".</summary>
    public const int MaximumLength = 32;

    /// <summary>Two characters is an initial, not a name anyone finds again in a list.</summary>
    public const int MinimumLength = 3;

    private static readonly Message LengthError =
        Message.Of("account.user-name.length", ("minimum", MinimumLength), ("count", MaximumLength));

    private static readonly Message CharactersError = Message.Of("account.user-name.characters");

    /// <summary>The form names are stored under and compared in: trimmed and upper-cased, culture
    /// independent, so "alice", "Alice " and "ALICE" are one account.</summary>
    public static string Normalize(string userName) => userName.Trim().ToUpperInvariant();

    /// <summary>Why <paramref name="userName"/> is refused, as a message the Api renders (ADR 0042), or <c>null</c>
    /// when it is acceptable.</summary>
    public static Message? Validate(string? userName)
    {
        var trimmed = userName?.Trim() ?? "";
        if (trimmed.Length is < MinimumLength or > MaximumLength)
            return LengthError;

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_'))
                return CharactersError;
        }

        return null;
    }
}
