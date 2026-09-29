using System.Globalization;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Infrastructure.Integrations;

/// <summary>
/// Makes text a service chose safe to put into a sentence the app shows and logs (MAM-7).
/// </summary>
/// <remarks>
/// A server name, a version or an error description is whatever the service at the owner's address
/// sends — a wrong address or a hostile service included. Left as it is, a line break splits the log
/// line it lands in, a right-to-left override (U+202E) makes the sentence read backwards on screen,
/// a megabyte of description fills the dialog, and a credential the service echoes goes wherever the
/// sentence goes. Every remote string that enters a message goes through <see cref="Clean"/> first.
/// </remarks>
internal static class RemoteText
{
    private const char Ellipsis = '…';

    /// <summary>
    /// <paramref name="value"/> with every one of <paramref name="secrets"/> masked
    /// (<see cref="SecretRedaction"/>), without control and format characters (Unicode Cc and Cf, bidi
    /// overrides and zero-width characters included), every run of whitespace as one space, trimmed,
    /// and cut to at most <paramref name="maximumLength"/> characters with a trailing <c>…</c>.
    /// Empty for <c>null</c>.
    /// </summary>
    /// <remarks>
    /// MAM-7 — masked before it is cut: the masking the adapter base class does to the finished
    /// sentence replaces only whole values, so a key that straddled the cut would survive there as its
    /// first characters. Masked twice: once as sent, where a password with a run of spaces or a
    /// character the cleaning drops is still whole, and again once the cleaning has joined a key a
    /// zero-width character split in two.
    /// </remarks>
    public static string Clean(string? value, int maximumLength, string?[] secrets)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);
        ArgumentNullException.ThrowIfNull(secrets);
        if (string.IsNullOrEmpty(value))
            return "";

        var masked = SecretRedaction.Redact(value, secrets)!;
        var builder = new StringBuilder(masked.Length);
        var pendingSpace = false;
        // By rune, so a surrogate pair is judged as the one character it is, and a lone surrogate
        // (which enumerates as U+FFFD) can't survive into the text.
        foreach (var rune in masked.EnumerateRunes())
        {
            // Whitespace first: a line break or a tab is a control character too, but it separated
            // two words, and dropping it would glue them together.
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format)
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        var cleaned = SecretRedaction.Redact(builder.ToString(), secrets)!;
        if (cleaned.Length <= maximumLength)
            return cleaned;

        var cut = maximumLength - 1;
        // Never end on half of a surrogate pair.
        if (cut > 0 && char.IsHighSurrogate(cleaned[cut - 1]))
            cut--;
        return cleaned[..cut].TrimEnd() + Ellipsis;
    }
}
