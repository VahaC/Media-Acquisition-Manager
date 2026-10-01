using System.Globalization;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-8 — the form library titles and paths are searched in: SQLite folds only ASCII letters, so the
// search compares copies upper-cased in .NET.
public sealed class LibrarySearchTextTests
{
    [Theory]
    [InlineData("чужі", "ЧУЖІ")]
    [InlineData("ґава, їжак і євген", "ҐАВА, ЇЖАК І ЄВГЕН")]
    [InlineData("Alien. Collection/[02.1] aliens (1986).mkv", "ALIEN. COLLECTION/[02.1] ALIENS (1986).MKV")]
    [InlineData("tt0090605", "TT0090605")]
    public void Normalize_upper_cases_every_letter(string value, string expected)
    {
        //Arrange

        //Act
        var normalized = LibrarySearchText.Normalize(value);

        //Assert
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Normalize_keeps_surrounding_and_repeated_whitespace()
    {
        //Arrange
        // A stored path stays exactly as reported; only a search term is trimmed, by its reader.
        const string value = " /media/movies/Aliens  (1986).mkv ";

        //Act
        var normalized = LibrarySearchText.Normalize(value);

        //Assert
        Assert.Equal(" /MEDIA/MOVIES/ALIENS  (1986).MKV ", normalized);
    }

    [Fact]
    public void Normalize_does_not_depend_on_the_current_culture()
    {
        //Arrange
        // Turkish upper-cases i to İ; a stored form that depended on the machine would never match.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        string normalized;

        //Act
        try
        {
            normalized = LibrarySearchText.Normalize("interstellar");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        //Assert
        Assert.Equal("INTERSTELLAR", normalized);
    }
}
