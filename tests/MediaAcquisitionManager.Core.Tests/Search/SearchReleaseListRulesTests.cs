using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Tests.Search;

// MAM-12 — what a stored search's release list may be filtered and sorted by: every value read from its
// query-string text into a typed filter, or refused with one sentence that echoes it.
public sealed class SearchReleaseListRulesTests
{
    private const string SourceId = "3f2c1b7e-9a4d-4e21-8c55-0d6b2f9e1a37";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_of_blank_values_filters_nothing_and_sorts_by_seeders(string? blank)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(blank, blank, blank, blank, blank, blank, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(new SearchReleaseFilter(null, null, false, null, false, null, null, SearchReleaseSort.Seeders), filter);
    }

    [Theory]
    [InlineData(SourceId)]
    [InlineData($"  {SourceId}  ")]
    [InlineData("3F2C1B7E-9A4D-4E21-8C55-0D6B2F9E1A37")]
    [InlineData("3f2c1b7e9a4d4e218c550d6b2f9e1a37")]
    public void Validate_reads_a_source_id(string source)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(source, null, null, null, null, null, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(Guid.Parse(SourceId), filter!.SourceId);
    }

    [Theory]
    [InlineData("toloka", "“toloka” is not a release source.")]
    [InlineData(" 3f2c1b7e-9a4d-4e21-8c55 ", "“3f2c1b7e-9a4d-4e21-8c55” is not a release source.")]
    public void Validate_refuses_a_source_that_is_not_an_id(string source, string expected)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(source, null, null, null, null, null, out var filter);

        //Assert
        Assert.Equal(expected, error);
        Assert.Null(filter);
    }

    [Theory]
    [InlineData("ukr", "ukr")]
    [InlineData("UKR", "ukr")]
    [InlineData(" Eng ", "eng")]
    [InlineData("mul", "mul")]
    public void Validate_reads_a_language_code_lower_cased(string language, string expected)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, language, null, null, null, null, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(expected, filter!.AudioLanguage);
        Assert.False(filter.WithoutAudioLanguage);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("UNKNOWN")]
    [InlineData(" Unknown ")]
    public void Validate_reads_unknown_as_no_language_claimed(string language)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, language, null, null, null, null, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Null(filter!.AudioLanguage);
        Assert.True(filter.WithoutAudioLanguage);
    }

    [Theory]
    [InlineData("uk")]
    [InlineData("ukra")]
    [InlineData("uk1")]
    [InlineData("укр")]
    [InlineData("u k")]
    // A Kelvin sign, which an ignore-case [a-z] would let through as a "k".
    [InlineData("Kor")]
    [InlineData("unknowns")]
    public void Validate_refuses_a_language_that_is_not_a_code(string language)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, language, null, null, null, null, out var filter);

        //Assert
        Assert.Equal($"“{language}” is not a language code. Use a three-letter code such as ukr, or unknown.", error);
        Assert.Null(filter);
    }

    [Theory]
    [InlineData("1080", 1080)]
    [InlineData(" 720 ", 720)]
    [InlineData("1", 1)]
    [InlineData("9999", 9999)]
    [InlineData("360", 360)]
    public void Validate_reads_a_resolution_class(string quality, int expected)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, null, quality, null, null, null, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(expected, filter!.Resolution);
        Assert.False(filter.WithoutResolution);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("UnKnOwN")]
    public void Validate_reads_unknown_as_no_resolution_claimed(string quality)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, null, quality, null, null, null, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Null(filter!.Resolution);
        Assert.True(filter.WithoutResolution);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10000")]
    [InlineData("1080p")]
    [InlineData("4K")]
    [InlineData("-1080")]
    [InlineData("+1080")]
    [InlineData("1,080")]
    // Arabic-Indic digits: ASCII only.
    [InlineData("١٠٨٠")]
    public void Validate_refuses_a_quality_that_is_not_a_resolution(string quality)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, null, quality, null, null, null, out var filter);

        //Assert
        Assert.Equal($"“{quality}” is not a resolution. Use one such as 1080, or unknown.", error);
        Assert.Null(filter);
    }

    [Theory]
    [InlineData("0", null, 0L, null)]
    [InlineData(" 1073741824 ", null, 1_073_741_824L, null)]
    [InlineData(null, "9223372036854775807", null, long.MaxValue)]
    [InlineData("4294967296", "4294967296", 4_294_967_296L, 4_294_967_296L)]
    [InlineData("1", "36711232962", 1L, 36_711_232_962L)]
    public void Validate_reads_the_size_bounds(string? minSizeBytes, string? maxSizeBytes, long? expectedMinimum, long? expectedMaximum)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, null, null, minSizeBytes, maxSizeBytes, null, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(expectedMinimum, filter!.MinimumSizeBytes);
        Assert.Equal(expectedMaximum, filter.MaximumSizeBytes);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.5")]
    [InlineData("1e9")]
    [InlineData("10GB")]
    [InlineData("1 000")]
    // One past long.MaxValue.
    [InlineData("9223372036854775808")]
    public void Validate_refuses_a_size_that_is_not_a_number_of_bytes(string size)
    {
        //Arrange

        //Act
        var minimumError = SearchReleaseListRules.Validate(null, null, null, size, null, null, out var minimumFilter);
        var maximumError = SearchReleaseListRules.Validate(null, null, null, null, size, null, out var maximumFilter);

        //Assert
        Assert.Equal($"“{size}” is not a size in bytes.", minimumError);
        Assert.Equal($"“{size}” is not a size in bytes.", maximumError);
        Assert.Null(minimumFilter);
        Assert.Null(maximumFilter);
    }

    [Fact]
    public void Validate_refuses_a_smallest_size_larger_than_the_largest()
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, null, null, "2000", "1999", null, out var filter);

        //Assert
        Assert.Equal("The smallest size can't be larger than the largest.", error);
        Assert.Null(filter);
    }

    [Theory]
    [InlineData("seeders", SearchReleaseSort.Seeders)]
    [InlineData("largest", SearchReleaseSort.Largest)]
    [InlineData("smallest", SearchReleaseSort.Smallest)]
    [InlineData("newest", SearchReleaseSort.Newest)]
    [InlineData("quality", SearchReleaseSort.Quality)]
    [InlineData("SEEDERS", SearchReleaseSort.Seeders)]
    [InlineData(" Largest ", SearchReleaseSort.Largest)]
    [InlineData("Newest", SearchReleaseSort.Newest)]
    public void Validate_reads_a_sort_order_in_any_letter_case(string sort, SearchReleaseSort expected)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, null, null, null, null, sort, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(expected, filter!.Sort);
    }

    [Theory]
    [InlineData("rating")]
    [InlineData("seeder")]
    [InlineData("1")]
    [InlineData("-seeders")]
    public void Validate_refuses_a_sort_order_it_does_not_know(string sort)
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, null, null, null, null, sort, out var filter);

        //Assert
        Assert.Equal($"“{sort}” is not a sort order. Use seeders, largest, smallest, newest or quality.", error);
        Assert.Null(filter);
    }

    [Fact]
    public void Validate_reads_every_filter_together()
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(SourceId, "UKR", "2160", "1000", "2000", "quality", out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(new SearchReleaseFilter(Guid.Parse(SourceId), "ukr", false, 2160, false, 1000, 2000, SearchReleaseSort.Quality), filter);
    }

    [Fact]
    public void Validate_names_the_first_value_that_is_wrong()
    {
        //Arrange

        //Act
        var error = SearchReleaseListRules.Validate(null, "uk", "1080p", "-1", null, "rating", out var filter);

        //Assert
        Assert.Equal("“uk” is not a language code. Use a three-letter code such as ukr, or unknown.", error);
        Assert.Null(filter);
    }

    [Fact]
    public void Validate_repeats_at_most_thirty_two_characters_of_a_wrong_value()
    {
        //Arrange
        var source = new string('x', 40);
        var sort = new string('y', 32);

        //Act
        var sourceError = SearchReleaseListRules.Validate(source, null, null, null, null, null, out _);
        var sortError = SearchReleaseListRules.Validate(null, null, null, null, null, sort, out _);

        //Assert
        Assert.Equal($"“{new string('x', 32)}…” is not a release source.", sourceError);
        Assert.Equal($"“{sort}” is not a sort order. Use seeders, largest, smallest, newest or quality.", sortError);
    }
}
