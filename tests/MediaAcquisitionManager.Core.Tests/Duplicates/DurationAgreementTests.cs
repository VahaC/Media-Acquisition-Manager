using MediaAcquisitionManager.Core.Duplicates;

namespace MediaAcquisitionManager.Core.Tests.Duplicates;

// MAM-20 — whether the measured durations of a possible match's files agree: the MAM-34 runtime tolerance, 2 minutes or
// 10 % of the longest file, whichever is larger, so the answer never depends on which file comes first (ADR 0012 — a
// duration is evidence, never a match).
public sealed class DurationAgreementTests
{
    [Fact]
    public void Of_no_duration_is_unknown()
    {
        //Arrange

        //Act
        var agree = DurationAgreement.Of([]);

        //Assert
        Assert.Null(agree);
    }

    [Fact]
    public void Of_one_duration_is_unknown()
    {
        //Arrange

        //Act
        var agree = DurationAgreement.Of([7_000_000]);

        //Assert
        Assert.Null(agree);
    }

    [Theory]
    // 10 and 12 minutes: 10 % of the longer is 72 seconds, so the 2 minutes decide.
    [InlineData(new long[] { 600_000, 720_000 }, true)]
    [InlineData(new long[] { 600_000, 720_001 }, false)]
    [InlineData(new long[] { 600_000, 600_000 }, true)]
    public void Of_short_files_agrees_within_two_minutes(long[] durationsMilliseconds, bool expected)
    {
        //Arrange

        //Act
        var agree = DurationAgreement.Of(durationsMilliseconds);

        //Assert
        Assert.Equal(expected, agree);
    }

    [Theory]
    // Feature length: 10 % of 7 000 seconds is 700 seconds, more than 2 minutes.
    [InlineData(new long[] { 6_300_000, 7_000_000 }, true)]
    [InlineData(new long[] { 6_299_999, 7_000_000 }, false)]
    [InlineData(new long[] { 6_500_000, 7_000_000 }, true)]
    public void Of_long_files_agrees_within_a_tenth_of_the_longer(long[] durationsMilliseconds, bool expected)
    {
        //Arrange

        //Act
        var agree = DurationAgreement.Of(durationsMilliseconds);

        //Assert
        Assert.Equal(expected, agree);
    }

    [Theory]
    [InlineData(new long[] { 7_000_000, 6_300_000 }, true)]
    [InlineData(new long[] { 7_000_000, 6_299_999 }, false)]
    [InlineData(new long[] { 720_000, 600_000 }, true)]
    [InlineData(new long[] { 720_001, 600_000 }, false)]
    public void Of_gives_the_same_answer_whichever_file_comes_first(long[] durationsMilliseconds, bool expected)
    {
        //Arrange

        //Act
        var agree = DurationAgreement.Of(durationsMilliseconds);

        //Assert
        Assert.Equal(expected, agree);
    }

    [Theory]
    // Only the longest and the shortest decide; one between them changes nothing.
    [InlineData(new long[] { 6_500_000, 6_300_000, 7_000_000 }, true)]
    [InlineData(new long[] { 6_900_000, 6_299_999, 7_000_000 }, false)]
    public void Of_three_durations_compares_the_longest_and_the_shortest(long[] durationsMilliseconds, bool expected)
    {
        //Arrange

        //Act
        var agree = DurationAgreement.Of(durationsMilliseconds);

        //Assert
        Assert.Equal(expected, agree);
    }
}
