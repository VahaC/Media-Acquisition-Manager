using MediaAcquisitionManager.Core.Duplicates;

namespace MediaAcquisitionManager.Core.Tests.Duplicates;

// MAM-37 — whether the copies of one work run so differently that the owner must look (Vasyl, 2026-10-08): the shortest
// measured copy strictly under three quarters of the longest, in whole milliseconds. The live numbers are production's
// ffprobe readings of 2026-10-08: a featurette and another film under a work's ids are wide, other cuts and other rips
// are not.
public sealed class RunningTimeSpreadTests
{
    [Theory]
    // Blade Runner 2049's 9 827 808 ms is divisible by 4: three quarters of it is exactly 7 370 856.
    [InlineData(7_370_856, 9_827_808)]
    [InlineData(3_000_000, 4_000_000)]
    public void IsWide_exactly_three_quarters_of_the_longest_is_not_wide(long shortestMilliseconds, long longestMilliseconds)
    {
        //Arrange

        //Act
        var wide = RunningTimeSpread.IsWide(shortestMilliseconds, longestMilliseconds);

        //Assert
        Assert.False(wide);
    }

    [Theory]
    [InlineData(7_370_855, 9_827_808)]
    [InlineData(2_999_999, 4_000_000)]
    public void IsWide_one_millisecond_under_three_quarters_of_the_longest_is_wide(long shortestMilliseconds, long longestMilliseconds)
    {
        //Arrange

        //Act
        var wide = RunningTimeSpread.IsWide(shortestMilliseconds, longestMilliseconds);

        //Assert
        Assert.True(wide);
    }

    [Theory]
    // Three quarters of 1 000 001 is 750 000.75: no rounding moves the boundary, whichever way it would round.
    [InlineData(750_000, true)]
    [InlineData(750_001, false)]
    public void IsWide_a_longest_not_divisible_by_four_decides_by_the_exact_share(long shortestMilliseconds, bool expected)
    {
        //Arrange
        const long longestMilliseconds = 1_000_001;

        //Act
        var wide = RunningTimeSpread.IsWide(shortestMilliseconds, longestMilliseconds);

        //Assert
        Assert.Equal(expected, wide);
    }

    [Theory]
    // «Ніж у воді» (1962) and the Polanski + Skolimowski featurette under its ids: 27.9 %.
    [InlineData(1_600_699, 5_731_959)]
    // Blade Runner's Final Cut and Blade Runner 2049, which Jellyfin gave the 1982 film's ids: 71.7 %.
    [InlineData(7_051_384, 9_827_808)]
    public void IsWide_a_featurette_or_another_film_under_a_works_ids_is_wide(long shortestMilliseconds, long longestMilliseconds)
    {
        //Arrange

        //Act
        var wide = RunningTimeSpread.IsWide(shortestMilliseconds, longestMilliseconds);

        //Assert
        Assert.True(wide);
    }

    [Theory]
    // Alien³'s theatrical cut and the Special Assembly Cut, the closest cuts in the library: 79.3 %.
    [InlineData(6_892_192, 8_695_834)]
    // Terminator 2: 88.0 %.
    [InlineData(8_243_392, 9_366_400)]
    // Aliens: 88.9 %.
    [InlineData(8_234_496, 9_267_029)]
    // Rambo: Last Blood: 88.6 %.
    [InlineData(5_368_416, 6_058_080)]
    // AvP: Requiem: 93.0 %.
    [InlineData(5_639_872, 6_065_248)]
    // Alien: Resurrection: 93.7 %.
    [InlineData(6_528_608, 6_968_704)]
    // Terminator Salvation: 97.7 %.
    [InlineData(6_886_752, 7_051_072)]
    // Alien: Romulus, a 1080p rip beside the 2160p remux.
    [InlineData(7_140_926, 7_141_927)]
    public void IsWide_another_cut_or_another_rip_is_not_wide(long shortestMilliseconds, long longestMilliseconds)
    {
        //Arrange

        //Act
        var wide = RunningTimeSpread.IsWide(shortestMilliseconds, longestMilliseconds);

        //Assert
        Assert.False(wide);
    }
}
