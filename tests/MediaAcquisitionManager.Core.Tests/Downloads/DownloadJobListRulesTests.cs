using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-14 — what the list of a user's download jobs may be filtered by: a status by its name in any letter
// case, read from its query-string text, or refused with one sentence that echoes it and names every status.
public sealed class DownloadJobListRulesTests
{
    private const string Statuses = "Use Created, Submitted, Downloading, Paused, Downloaded, Failed, NeedsLinking or Cancelled.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_of_a_blank_status_filters_nothing(string? blank)
    {
        //Arrange

        //Act
        var error = DownloadJobListRules.Validate(blank, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Null(filter);
    }

    [Theory]
    [InlineData("Created", DownloadJobStatus.Created)]
    [InlineData("submitted", DownloadJobStatus.Submitted)]
    [InlineData("DOWNLOADING", DownloadJobStatus.Downloading)]
    [InlineData(" Paused ", DownloadJobStatus.Paused)]
    [InlineData("downloaded", DownloadJobStatus.Downloaded)]
    [InlineData("Failed", DownloadJobStatus.Failed)]
    [InlineData("needslinking", DownloadJobStatus.NeedsLinking)]
    [InlineData("cancelled", DownloadJobStatus.Cancelled)]
    public void Validate_reads_a_status_by_its_name_in_any_letter_case(string status, DownloadJobStatus expected)
    {
        //Arrange

        //Act
        var error = DownloadJobListRules.Validate(status, out var filter);

        //Assert
        Assert.Null(error);
        Assert.Equal(expected, filter);
    }

    [Theory]
    [InlineData("3", "“3” is not a download status.")]
    [InlineData("-1", "“-1” is not a download status.")]
    [InlineData("Created, Failed", "“Created, Failed” is not a download status.")]
    [InlineData(" canceled ", "“canceled” is not a download status.")]
    [InlineData("Needs Linking", "“Needs Linking” is not a download status.")]
    [InlineData("downloading-now-and-then-again-and-more", "“downloading-now-and-then-again-a…” is not a download status.")]
    public void Validate_refuses_anything_but_a_status_name_and_echoes_it(string status, string expected)
    {
        //Arrange
        // By name only: Enum.TryParse would take a number, and OR a list into one value.

        //Act
        var error = DownloadJobListRules.Validate(status, out var filter);

        //Assert
        Assert.Equal($"{expected} {Statuses}", error);
        Assert.Null(filter);
    }

    [Fact]
    public void Validate_names_every_status_in_its_sentence()
    {
        //Arrange
        // Guard the sentence: a status a later ticket adds must be offered too.

        //Act
        var error = DownloadJobListRules.Validate("x", out _);

        //Assert
        Assert.All(Enum.GetNames<DownloadJobStatus>(), name => Assert.Contains($" {name}", error, StringComparison.Ordinal));
    }
}
