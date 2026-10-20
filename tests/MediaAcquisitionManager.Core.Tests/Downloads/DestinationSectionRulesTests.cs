using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-32 — the name a release source's section is listed and stored by (the gateway's "|-" subsection
// mark gone, trimmed, cut to its column), and what a section a destination's save maps may be: a source, a
// name and at least one category id, each id as a source gives one. Every refusal is pinned with the
// sentence the destination dialog shows.
public sealed class DestinationSectionRulesTests
{
    // Spelled out rather than read from DestinationSectionRules: they are what the dialog shows.
    private const string SourceMissingError = "Pick the release source of each section.";
    private const string NameMissingError = "A section needs its name.";
    private const string NameControlCharacterError = "A section's name can't contain line breaks or other control characters.";
    private const string CategoriesMissingError = "A section needs at least one category of its release source.";

    private static readonly Guid SourceId = new("5f0e8b4e-3c1a-4d55-9a57-0d1c2b3a4f60");

    [Theory]
    [InlineData("|-Мультсеріали", "Мультсеріали")]
    [InlineData("|- Фільми в HD", "Фільми в HD")]
    [InlineData("  |-Телесеріали  ", "Телесеріали")]
    [InlineData("|-|-Вкладений", "Вкладений")]
    [InlineData("TV/HD", "TV/HD")]
    [InlineData("DVD українською", "DVD українською")]
    [InlineData("Мультфільми |- і казки", "Мультфільми |- і казки")]
    public void CleanName_takes_the_subsection_mark_off_the_start_only(string name, string expected)
    {
        //Arrange

        //Act
        var cleaned = DestinationSectionRules.CleanName(name, "100125");

        //Assert
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    [InlineData("|-")]
    [InlineData("  ")]
    [InlineData("")]
    public void CleanName_of_a_name_that_is_nothing_but_the_mark_is_the_category_id(string name)
    {
        //Arrange

        //Act
        var cleaned = DestinationSectionRules.CleanName(name, "100125");

        //Assert
        Assert.Equal("100125", cleaned);
    }

    [Fact]
    public void CleanName_and_StoredName_cut_a_name_to_its_column()
    {
        //Arrange
        var name = "|-" + new string('я', DestinationSectionEntity.NameMaximumLength + 10);

        //Act
        var cleaned = DestinationSectionRules.CleanName(name, "100125");
        var stored = DestinationSectionRules.StoredName($" {new string('я', DestinationSectionEntity.NameMaximumLength + 10)} ");

        //Assert
        Assert.Equal(new string('я', DestinationSectionEntity.NameMaximumLength), cleaned);
        Assert.Equal(new string('я', DestinationSectionEntity.NameMaximumLength), stored);
    }

    [Fact]
    public void Validate_lets_a_section_with_a_source_a_name_and_category_ids_through()
    {
        //Arrange
        string?[] categoryIds = ["100125", "100044", "12345678901234567890123456789012"];

        //Act
        var error = DestinationSectionRules.Validate(SourceId, " Мультсеріали ", categoryIds);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(false, "Мультсеріали", "100125", SourceMissingError)]
    [InlineData(true, null, "100125", NameMissingError)]
    [InlineData(true, " \t", "100125", NameMissingError)]
    [InlineData(true, "Мульт\nсеріали", "100125", NameControlCharacterError)]
    [InlineData(true, "Мультсеріали", null, CategoriesMissingError)]
    [InlineData(true, "Мультсеріали", "", "“” is not a category id of a release source.")]
    [InlineData(true, "Мультсеріали", "100 125", "“100 125” is not a category id of a release source.")]
    [InlineData(true, "Мультсеріали", "5040,100125", "“5040,100125” is not a category id of a release source.")]
    [InlineData(true, "Мультсеріали", "100\u0000125", "“100\u0000125” is not a category id of a release source.")]
    [InlineData(true, "Мультсеріали", "123456789012345678901234567890123", "“12345678901234567890123456789012…” is not a category id of a release source.")]
    public void Validate_is_the_first_rule_broken(bool hasSource, string? name, string? categoryId, string expected)
    {
        //Arrange
        // No id means no list at all; a blank one is a list holding a bad id.
        IReadOnlyList<string?>? categoryIds = categoryId is null ? null : ["100044", categoryId];

        //Act
        var error = DestinationSectionRules.Validate(hasSource ? SourceId : null, name, categoryIds);

        //Assert
        Assert.Equal(expected, TestTexts.En(error));
    }

    [Fact]
    public void Validate_of_an_empty_category_list_is_the_rule()
    {
        //Arrange

        //Act
        var error = DestinationSectionRules.Validate(SourceId, "Мультсеріали", []);

        //Assert
        Assert.Equal(CategoriesMissingError, TestTexts.En(error));
    }
}
