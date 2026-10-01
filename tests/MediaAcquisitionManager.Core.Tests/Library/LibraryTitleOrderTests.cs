using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-8 — the order the Library page lists titles in, the same comparer the database collation and the
// API's choice of a work's title use: Ukrainian alphabetical order with letter case ignored, where code
// points would put І, Є and Ї before А and Ґ after Я, and the invariant culture would sort Ґ with Г.
public sealed class LibraryTitleOrderTests
{
    [Fact]
    public void Comparer_orders_the_letters_of_the_ukrainian_alphabet()
    {
        //Arrange
        const string alphabet = "а б в г ґ д е є ж з и і ї й к л м н о п р с т у ф х ц ч ш щ ь ю я";
        var shuffled = alphabet.Split(' ').Reverse().ToList();

        //Act
        var ordered = shuffled.Order(LibraryTitleOrder.Comparer);

        //Assert
        Assert.Equal(alphabet, string.Join(' ', ordered));
    }

    [Fact]
    public void Comparer_orders_titles_by_the_ukrainian_alphabet_then_latin_ones()
    {
        //Arrange
        string[] titles = ["Zorro", "Яга", "Alien", "Їжа", "Ірина", "Інтерстеллар", "Ґава", "Гнів", "Євген", "Ешка", "Аватар"];

        //Act
        var ordered = titles.Order(LibraryTitleOrder.Comparer);

        //Assert
        // Гнів before Ґава and Ірина before Їжа: Ґ and Ї are letters of their own, not variants of Г
        // and І that only break a tie.
        Assert.Equal(["Аватар", "Гнів", "Ґава", "Ешка", "Євген", "Інтерстеллар", "Ірина", "Їжа", "Яга", "Alien", "Zorro"], ordered);
    }

    [Theory]
    [InlineData("чужі", "ЧУЖІ")]
    [InlineData("Aliens", "aliens")]
    [InlineData("Ґава", "ґава")]
    public void Comparer_ignores_letter_case(string first, string second)
    {
        //Arrange

        //Act
        var comparison = LibraryTitleOrder.Comparer.Compare(first, second);

        //Assert
        Assert.Equal(0, comparison);
    }
}
