using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — whether a text built in code reads like words for a person: any Cyrillic letter, a typographic
/// quote around words or a hole, or at least three words of which at least two start lower-case, ending with terminal
/// punctuation. A hole of an interpolation (<c>{}</c>) counts as a word that isn't lower-case. Narrow on purpose: keys,
/// media types, formats, SQL, headers and a bare quote character (a title's apostrophe matched as data) pass; what it
/// flags is judged by where it goes (a log or an exception may hold English).
/// </summary>
public static partial class SentenceDetector
{
    private const string TypographicQuotes = "“”‘’«»„";

    private const string TerminalPunctuation = ".?!…:";

    private const string Hole = "{}";

    /// <summary>Whether <paramref name="text"/> reads like a sentence.</summary>
    public static bool IsSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (Cyrillic().IsMatch(text))
            return true;
        if (text.AsSpan().IndexOfAny(TypographicQuotes) >= 0
            && (text.Contains(Hole, StringComparison.Ordinal) || text.Any(character => char.IsUpper(character) || char.IsLower(character))))
            return true;

        var trimmed = text.TrimEnd();
        if (!TerminalPunctuation.Contains(trimmed[^1], StringComparison.Ordinal))
            return false;

        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 3 && words.Count(StartsLowerCase) >= 2;
    }

    /// <summary>Whether the first letter of <paramref name="word"/> is lower-case: <c>(error</c> is, <c>{}</c> isn't.</summary>
    private static bool StartsLowerCase(string word)
    {
        foreach (var character in word)
        {
            if (char.IsLetter(character))
                return char.IsLower(character);
        }

        return false;
    }

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex Cyrillic();
}
