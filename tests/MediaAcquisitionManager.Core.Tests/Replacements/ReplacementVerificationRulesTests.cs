using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-25 — the check's two waits (Vasyl, 2026-10-10: an hour from the moment the library was asked to look; ADR 0035): an
// hour each, reckoned from the times stored on the job, and run out at the hour exactly.
public sealed class ReplacementVerificationRulesTests
{
    private static readonly DateTime StartUtc = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);

    [Fact]
    public void The_files_and_library_waits_are_an_hour_each()
    {
        //Arrange

        //Act
        var waits = (ReplacementVerificationRules.FilesWait, ReplacementVerificationRules.LibraryWait);

        //Assert
        Assert.Equal((TimeSpan.FromHours(1), TimeSpan.FromHours(1)), waits);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3_599_999, false)]
    [InlineData(3_600_000, true)]
    [InlineData(7_200_000, true)]
    public void FilesWaitPassed_runs_out_at_the_hour_from_the_start_of_the_check(long elapsedMilliseconds, bool passed)
    {
        //Arrange
        var nowUtc = StartUtc.AddMilliseconds(elapsedMilliseconds);

        //Act
        var result = ReplacementVerificationRules.FilesWaitPassed(StartUtc, nowUtc);

        //Assert
        Assert.Equal(passed, result);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3_599_999, false)]
    [InlineData(3_600_000, true)]
    [InlineData(7_200_000, true)]
    public void LibraryWaitPassed_runs_out_at_the_hour_from_the_ask(long elapsedMilliseconds, bool passed)
    {
        //Arrange
        var nowUtc = StartUtc.AddMilliseconds(elapsedMilliseconds);

        //Act
        var result = ReplacementVerificationRules.LibraryWaitPassed(StartUtc, nowUtc);

        //Assert
        Assert.Equal(passed, result);
    }
}
