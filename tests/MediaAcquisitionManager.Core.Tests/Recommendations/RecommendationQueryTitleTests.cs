using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33 — the title a recommended work is searched for at the release sources by: its original title when every
// letter of it is Latin, digits and punctuation saying nothing of the script; else its catalog (Ukrainian) title.
public sealed class RecommendationQueryTitleTests
{
    [Theory]
    [InlineData("Інтерстеллар", "Interstellar", "Interstellar")]
    [InlineData("Амелі", "Le Fabuleux Destin d'Amélie Poulain", "Le Fabuleux Destin d'Amélie Poulain")]
    [InlineData("Шумахер 1994: Народження легенди", "Schumacher '94 - Die Geburt einer Legende", "Schumacher '94 - Die Geburt einer Legende")]
    [InlineData("Кар'єра Нікося Дизми", "Kariera Nikosia Dyzmy", "Kariera Nikosia Dyzmy")]
    [InlineData("Twoje imię", " Łódź ", "Łódź")]
    [InlineData("1917", "1917", "1917")]
    public void For_takes_an_original_title_written_in_latin_script(string title, string original, string expected)
    {
        //Arrange

        //Act
        var query = RecommendationQueryTitle.For(title, original);

        //Assert
        Assert.Equal(expected, query);
    }

    [Theory]
    [InlineData("Твоє ім'я", "君の名は。")]
    [InlineData("Колонія", "군체")]
    [InlineData("Брат", "Брат")]
    [InlineData("Високо висять червоні ліхтарі", "Da hong denglong gaogao gua 大紅燈籠高高掛")]
    [InlineData("Рядок", "!!!")]
    [InlineData("Рядок", "")]
    [InlineData("Рядок", null)]
    public void For_takes_the_catalog_title_when_the_original_is_in_another_script_or_has_no_letter(string title, string? original)
    {
        //Arrange

        //Act
        var query = RecommendationQueryTitle.For(title, original);

        //Assert
        Assert.Equal(title, query);
    }
}
