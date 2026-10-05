using System.Text;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — the title a recommended work is searched for at the release sources by: its original title when that
/// is written in Latin script, else its catalog (Ukrainian) title. The availability check sends it, and the list's
/// "Releases" link fills the Search page with it.
/// </summary>
/// <remarks>
/// <para>
/// Toloka's listings are "Ukrainian / Original (year)", and Prowlarr keeps the original part alone in the title
/// (VendorSamples), so a Latin original finds the most; the Ukrainian title is in the listing's description, which
/// Toloka's search doesn't read for every release. An original in another script — Japanese, Korean, Chinese — is
/// in neither part of the listing, where Toloka writes its romanisation or the Ukrainian title instead, so the
/// catalog title is the better guess there.
/// </para>
/// <para>
/// Latin means every letter is in one of Unicode's Latin blocks (Basic Latin to Latin Extended-E, Latin Extended
/// Additional): "Amélie", "Kariera Nikosia Dyzmy" and "Schumacher '94" are; "Леон" and "君の名は。" are not. Digits,
/// punctuation, spaces and combining marks say nothing of the script, and a title with no letter or digit at all
/// is no query.
/// </para>
/// </remarks>
public static class RecommendationQueryTitle
{
    /// <summary><paramref name="originalTitle"/>, trimmed, when it is <see cref="IsLatin"/>; else
    /// <paramref name="title"/>, trimmed.</summary>
    public static string For(string title, string? originalTitle)
    {
        ArgumentNullException.ThrowIfNull(title);
        return originalTitle is { } original && IsLatin(original) ? original.Trim() : title.Trim();
    }

    /// <summary>Whether <paramref name="text"/> has a letter or a digit, and every letter of it is Latin.</summary>
    public static bool IsLatin(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var meaningful = false;
        // By rune, not by char: a letter outside the Basic Multilingual Plane is two chars, neither a letter alone.
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsDigit(rune))
            {
                meaningful = true;
                continue;
            }

            if (!Rune.IsLetter(rune))
                continue;
            if (!IsLatinLetter(rune.Value))
                return false;
            meaningful = true;
        }

        return meaningful;
    }

    /// <summary>Whether a letter's code point is in a Latin block.</summary>
    private static bool IsLatinLetter(int codePoint) =>
        codePoint is <= 0x024F // Basic Latin, Latin-1 Supplement, Latin Extended-A and -B
            or >= 0x1E00 and <= 0x1EFF // Latin Extended Additional
            or >= 0x2C60 and <= 0x2C7F // Latin Extended-C
            or >= 0xA720 and <= 0xA7FF // Latin Extended-D
            or >= 0xAB30 and <= 0xAB6F; // Latin Extended-E
}
