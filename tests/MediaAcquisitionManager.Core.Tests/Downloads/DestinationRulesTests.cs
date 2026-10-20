using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-11 — what a destination's name and client destination key may be: a name like an integration's
// (trimmed, at most 64 characters, no control characters, compared in any letter case), and the client's
// own category name exactly as the client spells it, never trimmed. Every refusal is pinned with the
// sentence the Destinations dialog shows. MAM-30 — the key's sentences name a category or a folder (ADR 0039), and a
// name a folder client shows as the torrent's label can't hold a comma. MAM-31 — once the client is known, a missing key
// is asked for as its folder or its category, by its name.
public sealed class DestinationRulesTests
{
    // Spelled out rather than read from DestinationRules: they are what the dialog shows.
    private const string NameMissingError = "A destination needs a name.";
    private const string NameTooLongError = "A destination's name can be at most 64 characters.";
    private const string NameControlCharacterError = "A destination's name can't contain line breaks or other control characters.";
    private const string KeyMissingError = "Pick the category or folder of the download client this destination saves to.";
    private const string KeyTooLongError = "A category or folder can be at most 256 characters.";
    private const string KeyControlCharacterError = "A category or folder can't contain line breaks or other control characters.";
    private const string LabelCommaError = "A label can't contain a comma.";

    [Theory]
    [InlineData(null, NameMissingError)]
    [InlineData("", NameMissingError)]
    [InlineData("   ", NameMissingError)]
    [InlineData("\t\n", NameMissingError)]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345", NameTooLongError)]
    [InlineData("Movies\n4K", NameControlCharacterError)]
    [InlineData("Movies\u00004K", NameControlCharacterError)]
    public void ValidateName_is_the_rule(string? name, string expected)
    {
        //Arrange

        //Act
        var error = DestinationRules.ValidateName(name);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Theory]
    [InlineData(64, NameTooLongError)]
    [InlineData(63, NameControlCharacterError)]
    public void ValidateName_reads_the_length_before_the_characters(int letters, string expected)
    {
        //Arrange
        // A line break inside the name, where trimming can't take it away.
        var name = new string('a', letters / 2) + "\n" + new string('a', letters - (letters / 2));

        //Act
        var error = DestinationRules.ValidateName(name);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Theory]
    [InlineData("Фільми")]
    [InlineData("Movies 4K")]
    [InlineData("  Movies 4K  ")]
    [InlineData("Movies (4K, HDR)")]
    [InlineData("1234567890123456789012345678901234567890123456789012345678901234")]
    // Surrounding whitespace is no part of the name: 64 characters once trimmed.
    [InlineData("  1234567890123456789012345678901234567890123456789012345678901234  ")]
    public void ValidateName_accepts_a_name_of_up_to_64_characters_in_any_script(string name)
    {
        //Arrange

        //Act
        var error = DestinationRules.ValidateName(name);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(" Movies ", "MOVIES")]
    [InlineData("movies 4k", "MOVIES 4K")]
    [InlineData("Фільми", "ФІЛЬМИ")]
    [InlineData("Мультфільми для дітей", "МУЛЬТФІЛЬМИ ДЛЯ ДІТЕЙ")]
    public void NormalizeName_trims_and_upper_cases_whatever_the_culture(string name, string expected)
    {
        //Arrange
        // Turkish upper-cases "i" to a dotted "İ": a culture-sensitive normalisation would let "movies" and
        // "MOVIES" be two destinations of one client on a server with that locale.
        var original = CultureInfo.CurrentCulture;
        string normalized;

        //Act
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            normalized = DestinationRules.NormalizeName(name);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        //Assert
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null, KeyMissingError)]
    [InlineData("", KeyMissingError)]
    [InlineData("   ", KeyMissingError)]
    [InlineData("\t", KeyMissingError)]
    [InlineData("Movies\n", KeyControlCharacterError)]
    [InlineData("TV\u0000Shows", KeyControlCharacterError)]
    [InlineData("Movies\u007F", KeyControlCharacterError)]
    public void ValidateClientDestinationKey_is_the_rule(string? key, string expected)
    {
        //Arrange

        //Act
        var error = DestinationRules.ValidateClientDestinationKey(key);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Theory]
    [InlineData(DownloadDestinationKind.Folder, "Pick the folder of “Transmission” this destination saves to.")]
    [InlineData(DownloadDestinationKind.Category, "Pick the category of “Transmission” this destination saves to.")]
    // A type this build doesn't register says nothing of what its destinations are.
    [InlineData(null, KeyMissingError)]
    public void ValidateClientDestinationKey_of_a_known_client_asks_for_a_missing_key_in_the_words_of_its_kind(
        DownloadDestinationKind? kind, string expected)
    {
        //Arrange
        string?[] missing = [null, "", "   ", "\t"];

        //Act
        var errors = missing.Select(key => DestinationRules.ValidateClientDestinationKey(key, kind, "Transmission"));

        //Assert
        Assert.All(errors, error => Assert.Equal(expected, TestTexts.En(error)));
    }

    [Theory]
    [InlineData(DownloadDestinationKind.Folder)]
    [InlineData(DownloadDestinationKind.Category)]
    [InlineData(null)]
    public void ValidateClientDestinationKey_of_a_known_client_reads_a_given_key_by_the_same_rules(DownloadDestinationKind? kind)
    {
        //Arrange
        string[] keys = ["/downloads/movies", new string('a', 257), "Movies\n"];

        //Act
        var errors = keys.Select(key => DestinationRules.ValidateClientDestinationKey(key, kind, "Transmission"));

        //Assert
        Assert.Equal<string?>([null, KeyTooLongError, KeyControlCharacterError], errors.Select(TestTexts.En));
    }

    [Theory]
    [InlineData(257, "", KeyTooLongError)]
    // Never trimmed: the spaces are part of the client's name, so they count.
    [InlineData(255, "  ", KeyTooLongError)]
    // Both too long and with a line break: the length is read first.
    [InlineData(256, "\n", KeyTooLongError)]
    [InlineData(255, "\n", KeyControlCharacterError)]
    public void ValidateClientDestinationKey_counts_every_character_towards_256(int letters, string extra, string expected)
    {
        //Arrange
        var key = new string('a', letters) + extra;

        //Act
        var error = DestinationRules.ValidateClientDestinationKey(key);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("TV Shows")]
    [InlineData("Фільми")]
    // A qBittorrent subcategory.
    [InlineData("Movies/4K")]
    // A client may name a category with surrounding spaces; it is checked as it is.
    [InlineData(" Movies ")]
    public void ValidateClientDestinationKey_accepts_the_clients_name_as_it_is(string key)
    {
        //Arrange

        //Act
        var error = DestinationRules.ValidateClientDestinationKey(key);

        //Assert
        Assert.Null(error);
    }

    [Fact]
    public void ValidateClientDestinationKey_accepts_256_characters()
    {
        //Arrange
        var key = new string('a', 256);

        //Act
        var error = DestinationRules.ValidateClientDestinationKey(key);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    // A Transmission folder, as the client sees it, is a key like any other.
    [InlineData("/downloads/movies")]
    [InlineData(@"D:\Torrents\Movies")]
    public void ValidateClientDestinationKey_accepts_a_folder(string key)
    {
        //Arrange

        //Act
        var error = DestinationRules.ValidateClientDestinationKey(key);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Movies, 4K")]
    [InlineData(",")]
    [InlineData("Фільми,")]
    public void ValidateLabel_refuses_a_name_with_a_comma(string name)
    {
        //Arrange

        //Act
        var error = DestinationRules.ValidateLabel(name);

        //Assert
        // Transmission keeps a torrent's labels as one comma-separated text: the comma would make two labels.
        Assert.Equal(LabelCommaError, TestTexts.En(error));
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("Movies (4K; HDR)")]
    [InlineData("Фільми для дітей")]
    [InlineData("Movies/4K")]
    public void ValidateLabel_accepts_a_name_without_a_comma(string name)
    {
        //Arrange

        //Act
        var error = DestinationRules.ValidateLabel(name);

        //Assert
        Assert.Null(error);
    }
}
