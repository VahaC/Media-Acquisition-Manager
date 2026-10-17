using System.Text.Json;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Core.Tests.Integrations;

// MAM-46 — a metadata catalog's language: what a request may name (an entry of the catalog's own list, in any letter
// case) and the one a new catalog starts with (the owner's language, else the browser's, else en-US, brought to the
// closest entry). Against TMDb's real list (VendorSamples/Tmdb/configuration-primary-translations.json), whose English
// entries begin with en-AG — the trap a "first entry of the language" rule falls into.
public sealed class CatalogLanguageRulesTests
{
    private static readonly IReadOnlyList<string> Tmdb = JsonSerializer.Deserialize<string[]>(TmdbSamples.ConfigurationPrimaryTranslations())!;

    [Theory]
    [InlineData("uk-UA", "uk-UA")]
    [InlineData("en-US", "en-US")]
    // The tag itself in any letter case, as the list spells it.
    [InlineData("en-gb", "en-GB")]
    [InlineData("PT-br", "pt-BR")]
    [InlineData(" de-AT ", "de-AT")]
    // A bare language: its own region where the list has it…
    [InlineData("de", "de-DE")]
    [InlineData("fr", "fr-FR")]
    [InlineData("pt", "pt-PT")]
    // …English's own default rather than en-AG, the first English of the list…
    [InlineData("en", "en-US")]
    [InlineData("EN", "en-US")]
    // …else the language's first entry.
    [InlineData("uk", "uk-UA")]
    [InlineData("ja", "ja-JP")]
    [InlineData("zh", "zh-CN")]
    // A region the list hasn't: the language as a bare one.
    [InlineData("en-XX", "en-US")]
    [InlineData("uk-RU", "uk-UA")]
    [InlineData("de-LU", "de-DE")]
    // No language the list has, or none at all: English.
    [InlineData("xx", "en-US")]
    [InlineData("tlh-KL", "en-US")]
    [InlineData("*", "en-US")]
    [InlineData("", "en-US")]
    public void Map_brings_a_tag_to_the_closest_language_of_the_list(string tag, string expected)
    {
        //Arrange

        //Act
        var mapped = CatalogLanguageRules.Map(Tmdb, tag);

        //Assert
        Assert.Equal(expected, mapped);
    }

    [Fact]
    public void Map_of_every_language_of_the_list_is_that_language()
    {
        //Arrange

        //Act
        var mapped = Tmdb.Select(language => CatalogLanguageRules.Map(Tmdb, language)).ToList();

        //Assert
        Assert.Equal(Tmdb, mapped);
    }

    [Fact]
    public void Map_on_a_list_without_en_us_falls_back_to_its_first_entry()
    {
        //Arrange
        string[] languages = ["fr-FR", "uk-UA"];

        //Act
        var mapped = CatalogLanguageRules.Map(languages, "xx");

        //Assert
        Assert.Equal("fr-FR", mapped);
    }

    [Theory]
    // The owner's saved language decides, whatever the browser says.
    [InlineData("uk", "en-US,en;q=0.9", "uk-UA")]
    [InlineData("en", "uk-UA,uk;q=0.9", "en-US")]
    // None saved: the browser's first language, its region kept when the list has it.
    [InlineData(null, "uk-UA,uk;q=0.9,en-US;q=0.8,en;q=0.7", "uk-UA")]
    [InlineData(null, "en-GB,en;q=0.9", "en-GB")]
    [InlineData(null, "de-AT, en;q=0.5", "de-AT")]
    [InlineData(null, "uk", "uk-UA")]
    // The first, not the heaviest: a browser lists them in the user's order.
    [InlineData(null, "pt-BR;q=0.1,uk;q=0.9", "pt-BR")]
    // Empty entries and the wildcard name nothing.
    [InlineData(null, " , *;q=0.5, uk", "uk-UA")]
    [InlineData("  ", "uk", "uk-UA")]
    // Neither: English.
    [InlineData(null, null, "en-US")]
    [InlineData(null, "", "en-US")]
    [InlineData(null, "*", "en-US")]
    public void PickDefault_is_the_owners_language_else_the_browsers_else_english(string? userLanguage, string? acceptLanguage, string expected)
    {
        //Arrange

        //Act
        var picked = CatalogLanguageRules.PickDefault(Tmdb, userLanguage, acceptLanguage);

        //Assert
        Assert.Equal(expected, picked);
    }

    [Theory]
    [InlineData("uk-UA", "uk-UA")]
    [InlineData("uk-ua", "uk-UA")]
    [InlineData(" EN-us ", "en-US")]
    public void Validate_of_a_language_of_the_list_is_fine_and_spelled_as_the_list_spells_it(string language, string expected)
    {
        //Arrange

        //Act
        var error = CatalogLanguageRules.Validate(language, Tmdb, "TMDb", out var matched);

        //Assert
        Assert.Null(error);
        Assert.Equal(expected, matched);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_of_no_language_is_fine_and_names_none(string? language)
    {
        //Arrange

        //Act
        var catalogError = CatalogLanguageRules.Validate(language, Tmdb, "TMDb", out var catalogMatched);
        var otherError = CatalogLanguageRules.Validate(language, null, "Jellyfin", out var otherMatched);

        //Assert
        Assert.Equal((null, null), (catalogError, catalogMatched));
        Assert.Equal((null, null), (otherError, otherMatched));
    }

    [Theory]
    [InlineData("fr", "fr")]
    // A bare language is no tag of the list: the request must say which one.
    [InlineData("uk", "uk")]
    [InlineData("uk_UA", "uk_UA")]
    [InlineData("uk-UA&page=2", "uk-UA&page=2")]
    [InlineData("a-tag-far-longer-than-any-language-tag", "a-tag-far-longer-than-any-langua…")]
    public void Validate_of_a_language_the_list_hasnt_is_the_sentence_with_examples_from_the_list(string language, string echoed)
    {
        //Arrange

        //Act
        var error = CatalogLanguageRules.Validate(language, Tmdb, "TMDb", out var matched);

        //Assert
        Assert.Equal($"“{echoed}” is not one of TMDb's catalog languages. Use one of the tags its adapter type lists, such as en-US or uk-UA.", error);
        Assert.Null(matched);
    }

    [Fact]
    public void Validate_of_a_language_for_a_type_that_is_no_catalog_is_the_sentence()
    {
        //Arrange

        //Act
        var error = CatalogLanguageRules.Validate("uk-UA", null, "Jellyfin", out var matched);

        //Assert
        Assert.Equal("Jellyfin has no catalog language to choose. Leave catalogLanguage out.", error);
        Assert.Null(matched);
    }

    [Fact]
    public void Recorded_list_is_tmdbs_144_language_and_region_tags_each_once()
    {
        //Arrange

        //Act
        var distinct = Tmdb.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        //Assert
        Assert.Equal(144, Tmdb.Count);
        Assert.Equal(144, distinct);
        Assert.All(Tmdb, tag => Assert.Matches(@"^[a-z]{2}-[A-Z]{2}\z", tag));
        Assert.All(Tmdb, tag => Assert.True(tag.Length <= CatalogLanguageRules.MaximumLength));
        Assert.Contains(CatalogLanguageRules.Fallback, Tmdb);
    }
}
