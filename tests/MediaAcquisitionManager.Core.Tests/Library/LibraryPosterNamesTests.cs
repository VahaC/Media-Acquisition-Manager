using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-35 — the file names a library work's poster is served under: the item's id, a dash and its poster tag, each letters
// and digits only, then .jpg, and nothing that could name a path outside the posters' folder. The library list hands out a
// URL only for such a name, and the endpoint parses it back into the two halves it looks the poster up by.
public sealed class LibraryPosterNamesTests
{
    private const string YellowstoneId = "bad3e58c7b81156c7bccd6235599accd";
    private const string YellowstoneTag = "7060953c5bc967a2e947ecc514b77a37";

    [Theory]
    [InlineData(YellowstoneId)]
    [InlineData(YellowstoneTag)]
    [InlineData("a")]
    [InlineData("ABCdef0123")]
    public void IsPart_takes_letters_and_digits(string part)
    {
        //Arrange

        //Act
        var taken = LibraryPosterNames.IsPart(part);

        //Assert
        Assert.True(taken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad3e58c-7b81-156c-7bcc-d6235599accd")]
    [InlineData("applied-primary-1")]
    [InlineData("abc_def")]
    [InlineData("abc.jpg")]
    [InlineData("..")]
    [InlineData("abc/def")]
    [InlineData("abc\\def")]
    [InlineData("abc def")]
    [InlineData("abc\n")]
    [InlineData("аbc")]
    public void IsPart_refuses_anything_else(string? part)
    {
        //Arrange

        //Act
        var taken = LibraryPosterNames.IsPart(part);

        //Assert
        // A dash too, so the one between the halves is never ambiguous; a trailing line break (\z, not $), and a Cyrillic
        // "а" that only looks Latin.
        Assert.False(taken);
    }

    [Fact]
    public void IsPart_refuses_a_part_longer_than_sixty_four_characters()
    {
        //Arrange
        var longest = new string('a', 64);
        var longer = new string('a', 65);

        //Act
        var takes = (LibraryPosterNames.IsPart(longest), LibraryPosterNames.IsPart(longer));

        //Assert
        Assert.Equal((true, false), takes);
    }

    [Fact]
    public void FileNameOf_is_the_item_id_a_dash_and_the_tag_as_a_jpeg()
    {
        //Arrange

        //Act
        var fileName = LibraryPosterNames.FileNameOf(YellowstoneId, YellowstoneTag);

        //Assert
        Assert.Equal("bad3e58c7b81156c7bccd6235599accd-7060953c5bc967a2e947ecc514b77a37.jpg", fileName);
    }

    [Theory]
    [InlineData(YellowstoneId, null)]
    [InlineData(YellowstoneId, "")]
    [InlineData(YellowstoneId, "applied-primary-1")]
    [InlineData(YellowstoneId, "../x")]
    [InlineData(null, YellowstoneTag)]
    [InlineData("bad3e58c-7b81-156c-7bcc-d6235599accd", YellowstoneTag)]
    [InlineData("..", YellowstoneTag)]
    public void FileNameOf_is_null_for_a_half_the_endpoint_would_not_serve(string? sourceItemId, string? posterTag)
    {
        //Arrange

        //Act
        var fileName = LibraryPosterNames.FileNameOf(sourceItemId, posterTag);

        //Assert
        Assert.Null(fileName);
    }

    [Fact]
    public void TryParse_gives_back_the_item_id_and_the_tag_of_a_name_file_name_of_made()
    {
        //Arrange
        var fileName = LibraryPosterNames.FileNameOf(YellowstoneId, YellowstoneTag);

        //Act
        var parsed = LibraryPosterNames.TryParse(fileName, out var sourceItemId, out var posterTag);

        //Assert
        Assert.True(parsed);
        Assert.Equal((YellowstoneId, YellowstoneTag), (sourceItemId, posterTag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".jpg")]
    [InlineData("-.jpg")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd.jpg")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd-.jpg")]
    [InlineData("-7060953c5bc967a2e947ecc514b77a37.jpg")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd-7060953c5bc967a2e947ecc514b77a37")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd-7060953c5bc967a2e947ecc514b77a37.png")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd-7060953c5bc967a2e947ecc514b77a37.JPG")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd-7060953c5bc967a2e947ecc514b77a37.jpg\n")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd-7060953c-5bc9.jpg")]
    [InlineData("a-b-c.jpg")]
    [InlineData("../a-b.jpg")]
    [InlineData("..\\a-b.jpg")]
    [InlineData("C:a-b.jpg")]
    [InlineData("/a-b.jpg")]
    [InlineData("a-b.jpg/x")]
    [InlineData("a -b.jpg")]
    [InlineData("а-b.jpg")]
    public void TryParse_refuses_any_other_name(string? fileName)
    {
        //Arrange

        //Act
        var parsed = LibraryPosterNames.TryParse(fileName, out var sourceItemId, out var posterTag);

        //Assert
        Assert.False(parsed);
        Assert.Equal(("", ""), (sourceItemId, posterTag));
    }

    [Fact]
    public void TryParse_refuses_a_half_longer_than_sixty_four_characters()
    {
        //Arrange
        var longest = $"{new string('a', 64)}-{new string('b', 64)}.jpg";
        var longerItem = $"{new string('a', 65)}-b.jpg";
        var longerTag = $"a-{new string('b', 65)}.jpg";

        //Act
        var parsed = (
            LibraryPosterNames.TryParse(longest, out _, out _),
            LibraryPosterNames.TryParse(longerItem, out _, out _),
            LibraryPosterNames.TryParse(longerTag, out _, out _));

        //Assert
        Assert.Equal((true, false, false), parsed);
    }
}
