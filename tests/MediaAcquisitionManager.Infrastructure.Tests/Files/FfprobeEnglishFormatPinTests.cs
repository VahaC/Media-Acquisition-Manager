using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-47 (ADR 0042), design §3.3 — the per-producer pins of ffprobe's clauses: the time limit and the output cap, rendered
// through their real keys with today's inputs, asserting the English FfprobeProcess wrote before its words moved into
// files.json (its Describe with "0.#" and its DescribeBytes). Here rather than in the Api's EnglishFormatPinTests: the
// producer is internal to Infrastructure, and only this project sees it. FileInventoryMessages' N0 is pinned beside the
// walk (LocalFileInventoryListingTests.LimitReached_with_the_walks_own_limits_names_200000_files_and_100000_folders).
public sealed class FfprobeEnglishFormatPinTests
{
    [Theory]
    // Today's "0.#": one decimal at most, a midpoint rounded away from zero; minutes from a minute up.
    [InlineData(120_000, "it didn't finish within 2 minutes")]
    [InlineData(90_000, "it didn't finish within 1.5 minutes")]
    [InlineData(1_234_567, "it didn't finish within 20.6 minutes")]
    [InlineData(60_000, "it didn't finish within 1 minute")]
    [InlineData(15_000, "it didn't finish within 15 seconds")]
    [InlineData(59_950, "it didn't finish within 60 seconds")]
    [InlineData(59_940, "it didn't finish within 59.9 seconds")]
    [InlineData(1_000, "it didn't finish within 1 second")]
    [InlineData(500, "it didn't finish within 0.5 seconds")]
    [InlineData(250, "it didn't finish within 0.3 seconds")]
    [InlineData(150, "it didn't finish within 0.2 seconds")]
    [InlineData(125, "it didn't finish within 0.1 seconds")]
    // The one deliberate change (english-changes.md): a time shown as 1 that wasn't exactly 1 read "1 minutes".
    [InlineData(61_000, "it didn't finish within 1 minute")]
    public void TimedOut_writes_the_time_limit_to_one_decimal_as_its_format_did(int milliseconds, string english)
    {
        //Arrange
        var limit = TimeSpan.FromMilliseconds(milliseconds);

        //Act
        var clause = FfprobeProcess.TimedOut(limit);

        //Assert
        Assert.Equal(english, TestTexts.En(clause));
    }

    [Theory]
    // Today's DescribeBytes: MiB when the cap is whole MiB, else KiB when whole KiB, else bytes.
    [InlineData(FfprobeProcess.MaximumOutputBytes, "it printed more than 8 MiB")]
    [InlineData(4096, "it printed more than 4 KiB")]
    [InlineData(1536 * 1024, "it printed more than 1536 KiB")]
    [InlineData(1000, "it printed more than 1000 bytes")]
    // The one deliberate change (english-changes.md, design §8.7): one byte read "1 bytes".
    [InlineData(1, "it printed more than 1 byte")]
    public void PrintedTooMuch_writes_the_cap_in_its_exact_unit_as_DescribeBytes_did(int bytes, string english)
    {
        //Arrange
        var cap = bytes;

        //Act
        var clause = FfprobeProcess.PrintedTooMuch(cap);

        //Assert
        Assert.Equal(english, TestTexts.En(clause));
    }
}
