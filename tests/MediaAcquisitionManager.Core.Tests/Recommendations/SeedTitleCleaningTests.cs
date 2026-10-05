using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33 — the title a library work without a catalog id is looked up by: its display title cleaned of what a file
// name or a release's name adds around it, read from the reference library's own titles, and the year a bracketed
// year or span in it gives when its row gives none.
public sealed class SeedTitleCleaningTests
{
    [Theory]
    // A box set's numbering in front.
    [InlineData("120. Landing Stripling", "Landing Stripling")]
    [InlineData("02. Merry Madagascar", "Merry Madagascar")]
    // Underscores for spaces, and an episode marker that ends the work's title: what follows is the episode's name.
    [InlineData("Planet_Earth_E08 - Jungles", "Planet Earth")]
    // A release's whole name: a season marker, a span, the release type, a resolution, tags and a credit.
    [InlineData("The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ] by Jatx 11", "The Penguins Of Madagascar")]
    [InlineData("Yellowstone S01E02 1080p", "Yellowstone")]
    [InlineData("[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom]", "Aliens")]
    public void Clean_drops_what_a_file_name_adds_around_a_title(string title, string expected)
    {
        //Arrange

        //Act
        var cleaned = SeedTitleCleaning.Clean(title);

        //Assert
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    // Titles that only look like the markers keep every word.
    [InlineData("Stand by Me", "Stand by Me")]
    [InlineData("S1m0ne", "S1m0ne")]
    [InlineData("1917", "1917")]
    [InlineData("9.5 Weeks", "9.5 Weeks")]
    [InlineData("Ocean's Eleven", "Ocean's Eleven")]
    public void Clean_leaves_a_title_that_only_looks_like_a_marker_as_it_is(string title, string expected)
    {
        //Arrange

        //Act
        var cleaned = SeedTitleCleaning.Clean(title);

        //Assert
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    [InlineData("(2014) Interstellar", "(2014) Interstellar")]
    [InlineData("[1080p]", "")]
    [InlineData("12. ", "")]
    [InlineData("", "")]
    public void Clean_ends_a_title_only_at_a_marker_with_title_text_before_it(string title, string expected)
    {
        //Arrange

        //Act
        var cleaned = SeedTitleCleaning.Clean(title);

        //Assert
        // A marker in front ends nothing; nothing of a title left is an empty title, never looked up.
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    [InlineData("The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p", 2008)]
    [InlineData("[02.1] Aliens (1986) [Theatrical Cut]", 1986)]
    [InlineData("Yellowstone (2018–2024)", 2018)]
    [InlineData("120. Landing Stripling", null)]
    [InlineData("Planet_Earth_E08 - Jungles", null)]
    [InlineData("Blade Runner 2049", null)]
    public void BracketedYear_is_the_first_year_of_a_bracketed_year_or_span(string text, int? expected)
    {
        //Arrange

        //Act
        var year = SeedTitleCleaning.BracketedYear(text);

        //Assert
        Assert.Equal(expected, year);
    }
}
