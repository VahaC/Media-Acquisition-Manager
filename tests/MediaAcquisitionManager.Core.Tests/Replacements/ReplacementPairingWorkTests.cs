using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-47 (ADR 0042, WP12) — how the pairing's sentences name a work: its title and year as data, the way the Library
// names it in every language; a work with no title by a label of ours (a key), its year kept, so no English words
// reach a sentence as a name.
public sealed class ReplacementPairingWorkTests
{
    [Theory]
    [InlineData("Alien", 1979, "Alien (1979)")]
    [InlineData("Alien", null, "Alien")]
    [InlineData("Чужий", 1979, "Чужий (1979)")]
    public void NameOf_a_work_with_a_title_is_its_title_and_year_as_data(string title, int? year, string expected)
    {
        //Arrange

        //Act
        var name = ReplacementPairingWork.NameOf(title, year);

        //Assert
        Assert.Equal(new TextValue(expected), name);
    }

    [Theory]
    [InlineData(2005, "replacements.pairing.work.untitled-year", "a work with no title (2005)")]
    [InlineData(null, "replacements.pairing.work.untitled", "a work with no title")]
    public void NameOf_a_work_with_no_title_is_a_label_of_ours(int? year, string key, string english)
    {
        //Arrange

        //Act
        var name = ReplacementPairingWork.NameOf(null, year);

        //Assert
        var label = Assert.IsType<Message>(name);
        Assert.Equal((key, english), (label.Key, TestTexts.En(label)));
    }
}
