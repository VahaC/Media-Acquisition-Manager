using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Core.Tests.Files;

// MAM-42 — which tab of the Files in no library dialog a list request asks for: a view by its name in any letter case,
// read from its query-string text, blank for every counted file, or refused with one sentence that echoes it and names
// every view.
public sealed class UnreportedFileListRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateView_of_a_blank_view_lists_every_counted_file(string? blank)
    {
        //Arrange

        //Act
        var error = UnreportedFileListRules.ValidateView(blank, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Null(filter);
    }

    [Theory]
    [InlineData("Problems", UnreportedFileView.Problems)]
    [InlineData("problems", UnreportedFileView.Problems)]
    [InlineData(" IGNORED ", UnreportedFileView.Ignored)]
    [InlineData("Extras", UnreportedFileView.Extras)]
    public void ValidateView_reads_a_view_by_its_name_in_any_letter_case(string view, UnreportedFileView expected)
    {
        //Arrange

        //Act
        var error = UnreportedFileListRules.ValidateView(view, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(expected, filter);
    }

    [Theory]
    [InlineData("All", "All")]
    [InlineData("2", "2")]
    [InlineData("Problems, Ignored", "Problems, Ignored")]
    [InlineData("problems-and-then-a-lot-more-of-it", "problems-and-then-a-lot-more-of-…")]
    public void ValidateView_refuses_anything_but_a_view_name_and_echoes_it(string view, string echoed)
    {
        //Arrange
        // By name only: Enum.TryParse would take a number, and OR a list into one value.

        //Act
        var error = UnreportedFileListRules.ValidateView(view, out var filter);

        //Assert
        Assert.Equal($"“{echoed}” is not a view of the files in no library. Use Problems, Extras or Ignored, or leave it out for every file.", TestTexts.En(error));
        Assert.Null(filter);
    }

    [Fact]
    public void ValidateView_names_every_view_in_its_sentence()
    {
        //Arrange
        // Guard the sentence: a view a later ticket adds must be offered too.

        //Act
        var error = UnreportedFileListRules.ValidateView("x", out _);

        //Assert
        Assert.All(Enum.GetNames<UnreportedFileView>(), name => Assert.Contains($" {name}", TestTexts.En(error), StringComparison.Ordinal));
    }
}
