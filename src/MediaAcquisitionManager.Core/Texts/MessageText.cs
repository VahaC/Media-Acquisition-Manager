using System.Globalization;
using System.Text;

namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a name or a path a sentence quotes, made safe to sit inside it: what
/// <c>ReplacementText.Quote</c> did, without the quotes, which now belong to the template (<c>“{{path}}”</c> in English,
/// <c>«{{path}}»</c> in Ukrainian).
/// </summary>
public static class MessageText
{
    /// <summary>
    /// The most of a quoted name a sentence keeps: a path can be 4096 characters, and a reason is kept in 1000, so a deep
    /// path quoted whole would push the reason off its end. The start says where the file is; the row beside the
    /// sentence keeps it whole.
    /// </summary>
    public const int MaximumQuotedLength = 240;

    /// <summary>
    /// <paramref name="text"/> with every line break or tab as a space and without other control and format characters —
    /// a line break or a right-to-left override in a name a service chose would garble the sentence —, cut to
    /// <paramref name="maximumLength"/> with a trailing <c>…</c> at a character-pair boundary. Spaces are kept as they
    /// are: a file's name is quoted to be found. Empty for <c>null</c>.
    /// </summary>
    public static string Clean(string? text, int maximumLength = MaximumQuotedLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 2);
        var builder = new StringBuilder((text ?? "").Length);
        foreach (var character in text ?? "")
        {
            if (char.IsWhiteSpace(character))
                builder.Append(' ');
            else if (CharUnicodeInfo.GetUnicodeCategory(character) is not (UnicodeCategory.Control or UnicodeCategory.Format))
                builder.Append(character);
        }

        var cleaned = builder.ToString();
        if (cleaned.Length <= maximumLength)
            return cleaned;

        var cut = maximumLength - 1;
        // Never end on half of a surrogate pair.
        if (char.IsHighSurrogate(cleaned[cut - 1]))
            cut--;
        return cleaned[..cut] + "…";
    }
}
