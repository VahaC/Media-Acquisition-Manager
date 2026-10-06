using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-34 — how alike two titles are, for ordering a work's TMDb suggestions only (ADR 0012's matching stays exact): the
// Dice coefficient of character pairs, in the title keys' form, for any script.
public sealed class TitleSimilarityTests
{
    [Theory]
    [InlineData("Landing Stripling", "Landing Stripling")]
    [InlineData("Spider-Man: No Way Home", "spider man no way home")]
    [InlineData("Ocean's Eleven", "Oceans Eleven")]
    [InlineData("Планета Земля", "ПЛАНЕТА  ЗЕМЛЯ!")]
    [InlineData("X", "x")]
    public void Of_titles_of_one_key_is_one(string first, string second)
    {
        //Arrange

        //Act
        var similarity = TitleSimilarity.Of(first, second);

        //Assert
        Assert.Equal(1, similarity);
    }

    [Theory]
    [InlineData("", "Landing Stripling")]
    [InlineData("Landing Stripling", "")]
    [InlineData("!!!", "Landing Stripling")]
    [InlineData("", "")]
    public void Of_a_title_of_no_letter_or_digit_is_zero(string first, string second)
    {
        //Arrange

        //Act
        var similarity = TitleSimilarity.Of(first, second);

        //Assert
        Assert.Equal(0, similarity);
    }

    [Fact]
    public void Of_titles_with_no_pair_in_common_is_zero()
    {
        //Arrange

        //Act
        var similarity = TitleSimilarity.Of("ABC", "XYZ");

        //Assert
        Assert.Equal(0, similarity);
    }

    [Fact]
    public void Of_counts_the_pairs_both_titles_have_by_dice()
    {
        //Arrange
        // NIGHT: NI IG GH HT; NACHT: NA AC CH HT — one pair of eight in common.

        //Act
        var similarity = TitleSimilarity.Of("Night", "Nacht");

        //Assert
        Assert.Equal(0.25, similarity, precision: 10);
    }

    [Fact]
    public void Of_counts_a_repeated_pair_as_often_as_both_titles_have_it()
    {
        //Arrange
        // AAAA: AA AA AA; AA: AA — one in common, four in all.

        //Act
        var similarity = TitleSimilarity.Of("aaaa", "aa");

        //Assert
        Assert.Equal(0.5, similarity, precision: 10);
    }

    [Fact]
    public void Of_ranks_a_title_cut_short_above_an_unrelated_one()
    {
        //Arrange
        const string library = "Planet Earth";

        //Act
        var cutShort = TitleSimilarity.Of(library, "Planet Earth: The Complete Collection");
        var unrelated = TitleSimilarity.Of(library, "Landing Stripling");

        //Assert
        Assert.InRange(cutShort, 0.4, 0.99);
        Assert.True(cutShort > unrelated, $"{cutShort} should rank above {unrelated}.");
    }

    [Fact]
    public void Of_compares_a_cyrillic_title_letter_by_letter()
    {
        //Arrange

        //Act
        var close = TitleSimilarity.Of("Пінгвіни Мадагаскару", "Пінгвіни з Мадагаскару");
        var far = TitleSimilarity.Of("Пінгвіни Мадагаскару", "Планета Земля");

        //Assert
        Assert.InRange(close, 0.8, 0.99);
        Assert.True(close > far, $"{close} should rank above {far}.");
    }

    [Fact]
    public void Of_is_the_same_both_ways()
    {
        //Arrange

        //Act
        var forward = TitleSimilarity.Of("Planet Earth", "Planet Earth II");
        var backward = TitleSimilarity.Of("Planet Earth II", "Planet Earth");

        //Assert
        Assert.Equal(forward, backward);
    }
}
