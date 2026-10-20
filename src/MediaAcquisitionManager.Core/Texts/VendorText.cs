using System.Globalization;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — another program's words inside one of our sentences: a service's answer, the operating system's
/// or ffprobe's explanation, .NET's binder message. Shown as they are in every language, inside the template's quotes.
/// </summary>
/// <remarks>
/// <para>
/// MAM-7 — a server name, a version or an error description is whatever the service at the owner's address sends — a
/// wrong address or a hostile service included. Left as it is, a line break splits the log line it lands in, a
/// right-to-left override (U+202E) makes the sentence read backwards on screen, a megabyte of description fills the
/// dialog, and a credential the service echoes goes wherever the sentence goes.
/// </para>
/// <para>
/// MAM-47 — so the constructor is private: <see cref="Clean"/> is the only way to make one from raw words, and a slot a
/// template hints <c>vendor</c> can't hold unmasked text. <see cref="Stored"/> is only for text that was masked when it
/// was written (the database, stored English the converter parses).
/// </para>
/// </remarks>
public sealed record VendorText : MessageValue
{
    private const char Ellipsis = '…';

    private VendorText(string text) => Text = text;

    /// <summary>The cleaned, masked words.</summary>
    public string Text { get; }

    /// <summary>
    /// <paramref name="raw"/> with every one of <paramref name="secrets"/> masked (<see cref="SecretRedaction"/>),
    /// without control and format characters (Unicode Cc and Cf, bidi overrides and zero-width characters included),
    /// every run of whitespace as one space, trimmed, and cut to at most <paramref name="maximumLength"/> characters with
    /// a trailing <c>…</c>. Empty for <c>null</c>.
    /// </summary>
    /// <remarks>
    /// MAM-7 — masked before it is cut: the masking the adapter base class does to the finished sentence replaces only
    /// whole values, so a key that straddled the cut would survive there as its first characters. Masked twice: once as
    /// sent, where a password with a run of spaces or a character the cleaning drops is still whole, and again once the
    /// cleaning has joined a key a zero-width character split in two. MAM-47 — moved here from
    /// <c>Infrastructure/Integrations/RemoteText</c>, which now delegates to it.
    /// </remarks>
    public static VendorText Clean(string? raw, int maximumLength, params string?[] secrets)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);
        ArgumentNullException.ThrowIfNull(secrets);
        if (string.IsNullOrEmpty(raw))
            return new VendorText("");

        var masked = SecretRedaction.Redact(raw, secrets)!;
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
            return new VendorText(cleaned);

        var cut = maximumLength - 1;
        // Never end on half of a surrogate pair.
        if (cut > 0 && char.IsHighSurrogate(cleaned[cut - 1]))
            cut--;
        return new VendorText(cleaned[..cut].TrimEnd() + Ellipsis);
    }

    /// <summary>
    /// Only for text read back from the database or parsed from stored English, which was masked when it was written.
    /// </summary>
    /// <remarks>MAM-47 — its callers are pinned to <see cref="MessageJson"/> and the converter's parser by the IL scan
    /// (NoLiteralSentenceTests): anywhere else it would let raw words into a vendor slot.</remarks>
    public static VendorText Stored(string text) => new(text ?? throw new ArgumentNullException(nameof(text)));

    /// <summary>
    /// These words with every one of <paramref name="secrets"/> masked again — the Api's second pass, where today's
    /// <see cref="SecretRedaction.Redact"/> runs over a finished sentence.
    /// </summary>
    public VendorText Redacted(params string?[] secrets) => new(SecretRedaction.Redact(Text, secrets) ?? "");

    /// <summary>MAM-47 — the same kind of value holding <paramref name="text"/>: <see cref="MessageFit"/> cuts the
    /// words of a value it has to shorten, and a vendor's words stay a vendor's after the cut.</summary>
    internal VendorText WithText(string text) => new(text);
}
