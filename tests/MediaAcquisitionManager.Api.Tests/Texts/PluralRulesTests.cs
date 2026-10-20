using System.Globalization;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the server counts as the web app does. Each expected category is what node 24.18.1's
// Intl.PluralRules printed for the same number (re-run for MAM-47 on 2026-10-19):
//   new Intl.PluralRules('uk').select(n), new Intl.PluralRules('en').select(n)
public sealed class PluralRulesTests
{
    [Theory]
    [InlineData("0", PluralCategory.Many, PluralCategory.Other)]
    [InlineData("1", PluralCategory.One, PluralCategory.One)]
    [InlineData("2", PluralCategory.Few, PluralCategory.Other)]
    [InlineData("4", PluralCategory.Few, PluralCategory.Other)]
    [InlineData("5", PluralCategory.Many, PluralCategory.Other)]
    [InlineData("11", PluralCategory.Many, PluralCategory.Other)]
    [InlineData("12", PluralCategory.Many, PluralCategory.Other)]
    [InlineData("14", PluralCategory.Many, PluralCategory.Other)]
    [InlineData("21", PluralCategory.One, PluralCategory.Other)]
    [InlineData("22", PluralCategory.Few, PluralCategory.Other)]
    [InlineData("25", PluralCategory.Many, PluralCategory.Other)]
    [InlineData("101", PluralCategory.One, PluralCategory.Other)]
    [InlineData("111", PluralCategory.Many, PluralCategory.Other)]
    [InlineData("1.5", PluralCategory.Other, PluralCategory.Other)]
    [InlineData("0.5", PluralCategory.Other, PluralCategory.Other)]
    [InlineData("2.5", PluralCategory.Other, PluralCategory.Other)]
    public void Of_matches_intl_plural_rules(string number, PluralCategory ukrainian, PluralCategory english)
    {
        //Arrange
        var value = new NumberValue(decimal.Parse(number, CultureInfo.InvariantCulture));

        //Act
        var inUkrainian = PluralRules.Of(TextLanguage.Ukrainian, value.Number);
        var inEnglish = PluralRules.Of(TextLanguage.English, value.Number);
        var inPseudo = PluralRules.Of(TextLanguage.Pseudo, value.Number);

        //Assert
        Assert.Equal(ukrainian, inUkrainian);
        Assert.Equal(english, inEnglish);
        Assert.Equal(english, inPseudo);
    }

    [Fact]
    public void Of_counts_a_number_as_shown_so_one_point_zero_is_one()
    {
        //Arrange
        var value = new NumberValue(1.0m);

        //Act
        var category = PluralRules.Of(TextLanguage.English, value.Number);

        //Assert
        Assert.Equal(PluralCategory.One, category);
    }

    [Theory]
    [InlineData(PluralCategory.One, "_one")]
    [InlineData(PluralCategory.Few, "_few")]
    [InlineData(PluralCategory.Many, "_many")]
    [InlineData(PluralCategory.Other, "_other")]
    public void SuffixOf_is_the_key_suffix_the_locale_files_use(PluralCategory category, string suffix)
    {
        //Arrange

        //Act
        var written = PluralRules.SuffixOf(category);

        //Assert
        Assert.Equal(suffix, written);
    }
}
