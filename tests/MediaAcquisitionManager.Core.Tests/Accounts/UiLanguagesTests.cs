using MediaAcquisitionManager.Core.Accounts;

namespace MediaAcquisitionManager.Core.Tests.Accounts;

// MAM-46 — the languages a user can save for the web app: exactly the locales it has, as bare codes.
public sealed class UiLanguagesTests
{
    [Fact]
    public void Supported_is_english_then_ukrainian()
    {
        //Arrange

        //Act
        var supported = UiLanguages.Supported;

        //Assert
        Assert.Equal(["en", "uk"], supported);
        Assert.All(supported, language => Assert.True(language.Length <= UiLanguages.MaximumLength));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("uk", true)]
    [InlineData("UK", false)]
    [InlineData("uk-UA", false)]
    [InlineData(" uk", false)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_takes_a_supported_code_exactly(string? language, bool expected)
    {
        //Arrange

        //Act
        var supported = UiLanguages.IsSupported(language);

        //Assert
        Assert.Equal(expected, supported);
    }
}
