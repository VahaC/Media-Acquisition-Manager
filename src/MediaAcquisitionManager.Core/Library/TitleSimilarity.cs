using System.Text;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-34 — how alike two titles are, from 0 to 1, for ordering a library work's TMDb suggestions: the Dice coefficient
/// of the two titles' character pairs, compared in their <see cref="TitleMatchKey"/> form.
/// </summary>
/// <remarks>
/// <para>
/// Ranking only, never a match: ADR 0012's matching stays exact keys, and nothing is linked because two titles are
/// close — the owner picks. Character pairs rather than words, so a title cut short ("Planet Earth" against "Planet
/// Earth: The Complete Collection") or written with a slip still ranks above an unrelated one; by rune, so a Cyrillic or
/// any other title is compared letter by letter like a Latin one.
/// </para>
/// <para>
/// Equal keys are 1 (a one-letter title has no pair to compare); an empty key — a title of no letter or digit — is 0
/// against anything.
/// </para>
/// </remarks>
public static class TitleSimilarity
{
    /// <summary>How alike <paramref name="first"/> and <paramref name="second"/> are: 1 for one title, 0 for nothing in
    /// common.</summary>
    public static double Of(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var firstKey = TitleMatchKey.For(first);
        var secondKey = TitleMatchKey.For(second);
        if (firstKey.Length == 0 || secondKey.Length == 0)
            return 0;
        if (string.Equals(firstKey, secondKey, StringComparison.Ordinal))
            return 1;

        var firstPairs = Pairs(firstKey);
        var secondPairs = Pairs(secondKey);
        var total = firstPairs.Values.Sum() + secondPairs.Values.Sum();
        if (total == 0)
            return 0;

        // A pair counts as often as both titles have it: "AA" twice in one and once in the other is one in common.
        var shared = firstPairs.Sum(pair => Math.Min(pair.Value, secondPairs.GetValueOrDefault(pair.Key)));
        return 2.0 * shared / total;
    }

    // Each pair of neighbouring runes of the key, counted.
    private static Dictionary<string, int> Pairs(string key)
    {
        var runes = key.EnumerateRunes().ToArray();
        var pairs = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < runes.Length; index++)
        {
            var pair = new StringBuilder(4).Append(runes[index].ToString()).Append(runes[index + 1].ToString()).ToString();
            pairs[pair] = pairs.GetValueOrDefault(pair) + 1;
        }

        return pairs;
    }
}
