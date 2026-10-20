namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — which plural form a count takes, by CLDR's rules — the ones <c>Intl.PluralRules</c> applies in the
/// web app, so a server sentence and the page around it count alike (pinned against node in PluralRulesTests).
/// </summary>
/// <remarks>Hand-coded rather than read from ICU: the Windows dev box, the Linux image and their ICU versions then can't
/// disagree, and two languages need four lines.</remarks>
public static class PluralRules
{
    /// <summary>The form <paramref name="number"/> takes in <paramref name="language"/> (the pseudo-language counts as
    /// English). Visible fraction digits count: a <see cref="Core.Texts.NumberValue"/> has none it doesn't show.</summary>
    public static PluralCategory Of(TextLanguage language, decimal number)
    {
        var fractionDigits = number.Scale;
        var integer = decimal.Truncate(Math.Abs(number));
        return language switch
        {
            TextLanguage.Ukrainian => Ukrainian(integer, fractionDigits),
            // en: one = i = 1 and v = 0.
            _ => integer == 1 && fractionDigits == 0 ? PluralCategory.One : PluralCategory.Other,
        };
    }

    /// <summary>The key suffix of <paramref name="category"/>: <c>_one</c>, <c>_few</c>, <c>_many</c>, <c>_other</c>.</summary>
    public static string SuffixOf(PluralCategory category) => category switch
    {
        PluralCategory.One => "_one",
        PluralCategory.Few => "_few",
        PluralCategory.Many => "_many",
        _ => "_other",
    };

    // uk: one = v = 0 and i % 10 = 1 and i % 100 != 11; few = v = 0 and i % 10 = 2..4 and i % 100 != 12..14;
    // many = v = 0 and the rest; other = fractions.
    private static PluralCategory Ukrainian(decimal integer, int fractionDigits)
    {
        if (fractionDigits != 0)
            return PluralCategory.Other;

        var lastDigit = integer % 10;
        var lastTwoDigits = integer % 100;
        if (lastDigit == 1 && lastTwoDigits != 11)
            return PluralCategory.One;
        if (lastDigit is >= 2 and <= 4 && lastTwoDigits is not (>= 12 and <= 14))
            return PluralCategory.Few;
        return PluralCategory.Many;
    }
}
