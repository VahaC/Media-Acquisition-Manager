using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using static MediaAcquisitionManager.TestInfrastructure.Replacements.ReplacementPairingInputs;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042), design §3.3 — per-producer pins: each producer whose number, size or time a hint now writes,
// rendered through its real key with today's inputs, asserting the English it wrote before its sentences moved into the
// locale files. The hint carries the format, so a slip in a template's hint shows up here by its producer's name. The
// package that converts a producer adds its pins (WP2: NewFileReadability's N0 byte counts, the pairing's Clock and
// CountWord; WP3: the adapters' seconds and minutes, ffprobe, DescribeBytes, the holdings limit, FileInventoryMessages).
public sealed class EnglishFormatPinTests
{
    private const string OldAlien = "/media/movies/Alien (1979)/Alien.1979.1080p.mkv";
    private const string NewAlien = "/media/movies/Alien (1979)/Alien.1979.2160p.mkv";

    /// <summary>Jellyfin's stored type id (the adapter class is internal to Infrastructure).</summary>
    private const string JellyfinTypeId = "jellyfin";

    /// <summary>qBittorrent's stored type id.</summary>
    private const string QBittorrentTypeId = "qbittorrent";

    /// <summary>Transmission's stored type id.</summary>
    private const string TransmissionTypeId = "transmission";

    /// <summary>What the download clients' listing of every torrent may grow to: <c>MaximumResponseBytes</c>, 4 MiB
    /// (internal to Infrastructure).</summary>
    private const int HoldingsLimitBytes = 4 * 1024 * 1024;

    [Theory]
    // Today's {bytes:N0}: grouped from 1,000 with commas, never a size (as sizes both would read 1.18 MiB).
    [InlineData(1_234_567, 1_234_560, "1,234,567 bytes", "1,234,560 bytes")]
    [InlineData(999, 1_000, "999 bytes", "1,000 bytes")]
    [InlineData(100_000, 3_891_719_630, "100,000 bytes", "3,891,719,630 bytes")]
    // The one deliberate change (english-changes.md): one byte was "1 bytes".
    [InlineData(1, 2, "1 byte", "2 bytes")]
    public void NewFileReadability_size_reason_groups_exact_byte_counts_as_N0_did(long onDisk, long downloaded, string onDiskText, string downloadedText)
    {
        //Arrange
        var inspection = new FileInventoryResult<MediaInspection>(
            FileInventoryStatus.Found, new MediaInspection(TestMedia.File(inode: 2, sizeBytes: onDisk), TestMedia.Taken()), null);

        //Act
        var result = NewFileReadability.Check(NewAlien, inspection, downloaded, [], "qBittorrent");

        //Assert
        Assert.Equal(
            $"“{NewAlien}” is {onDiskText} on the disk, not the {downloadedText} “qBittorrent” downloaded, so it may be another file.",
            TestTexts.En(result.Problem));
    }

    [Theory]
    // Today's Clock: hours always shown and unbounded, minutes and seconds in two digits, truncated.
    [InlineData(4_324_000, 6_990_000, "1:12:04", "1:56:30")]
    [InlineData(1_800_000, 2_700_000, "0:30:00", "0:45:00")]
    [InlineData(4_324_999, 6_990_999, "1:12:04", "1:56:30")]
    [InlineData(1_000, 360_000_000, "0:00:01", "100:00:00")]
    public void ReplacementPairing_running_time_reason_writes_both_times_as_Clock_did(
        long newMilliseconds, long oldMilliseconds, string newText, string oldText)
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien, durationMilliseconds: oldMilliseconds);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")), durationMilliseconds: newMilliseconds);

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            $"“{NewAlien}” runs {newText}, less than three quarters of the old copy's {oldText}.",
            TestTexts.En(result.Reason));
    }

    [Theory]
    // Today's CountWord: Two to Ten spelled at the sentence's start, digits from 11.
    [InlineData(2, "Two")]
    [InlineData(3, "Three")]
    [InlineData(10, "Ten")]
    [InlineData(11, "11")]
    public void ReplacementPairing_new_files_reason_spells_the_count_at_its_start_as_CountWord_did(int count, string word)
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var files = Enumerable.Range(0, count)
            .Select(position => NewFile(position, $"/media/movies/Alien (1979)/Alien.1979.{position}.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348"))))
            .ToList();

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], files, [], LibraryName, libraryWaitPassed: false);

        //Assert
        var reason = TestTexts.En(result.Reason);
        Assert.StartsWith($"{word} new files are Alien (1979): “/media/movies/Alien (1979)/Alien.1979.0.mkv”", reason, StringComparison.Ordinal);
        Assert.EndsWith(
            $" and “/media/movies/Alien (1979)/Alien.1979.{count - 1}.mkv”; the app can't tell which one replaces the old copy.", reason, StringComparison.Ordinal);
    }

    [Theory]
    // Today's {seconds:0.##}: the production client's 15 (and its connect timeout's 5), a fraction as it is, at most two
    // decimals with a midpoint away from zero.
    [InlineData(15_000, "15 seconds")]
    [InlineData(2_500, "2.5 seconds")]
    [InlineData(100, "0.1 seconds")]
    [InlineData(125, "0.13 seconds")]
    [InlineData(333, "0.33 seconds")]
    public async Task HttpIntegrationAdapter_timeout_sentence_writes_the_seconds_to_two_decimals_as_its_format_did(int milliseconds, string seconds)
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(milliseconds));
        // A cancellation nobody asked for is the client's timeout (HttpClient's own rule), so nothing waits for it.
        harness.JellyfinHttp.Handler = (_, _) => throw new TaskCanceledException("The request timed out.");
        var settings = new IntegrationConnectionSettings(null, JellyfinIdentifyHarness.BaseUrl, null, null, JellyfinIdentifyHarness.ApiKey);

        //Act
        var result = await harness.Adapter(JellyfinTypeId).TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal($"Jellyfin at http://jellyfin.test:8096 didn't answer within {seconds}.", TestTexts.En(result.Message));
    }

    [Theory]
    // Today's {minutes:0.####}: the production Apply client's 20, a fraction as it is, a test's 100 ms as 0.0017.
    [InlineData(1_200_000, "20 minutes")]
    [InlineData(90_000, "1.5 minutes")]
    [InlineData(100, "0.0017 minutes")]
    public async Task JellyfinAdapter_apply_timeout_sentence_writes_the_minutes_to_four_decimals_as_its_format_did(int milliseconds, string minutes)
    {
        //Arrange
        using var jellyfin = new JellyfinIdentifyHarness(TimeSpan.FromMilliseconds(milliseconds));
        jellyfin.Server.Intercept = request =>
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/Items/RemoteSearch/Apply/", StringComparison.Ordinal)
                ? throw new TaskCanceledException("The request timed out.")
                : null;

        //Act
        var result = await jellyfin.IdentifyAsync(JellyfinIdentifyHarness.FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, result.Value!.Outcome);
        Assert.Equal(
            $"The match was sent to Jellyfin, but it didn't answer within {minutes}, and the item read again doesn't show it. Jellyfin may still be working on it: Retry in a few minutes checks again before sending anything.",
            TestTexts.En(result.Value.Sentence));
    }

    [Fact]
    // Today's "{MaximumResponseBytes / (1024 * 1024)} MiB": the limit is now a size, which the exact-unit rule writes
    // as the same whole MiB.
    public async Task QBittorrentAdapter_holdings_too_large_sentence_writes_the_limit_in_MiB_as_its_format_did()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var qbittorrent = new FakeQBittorrentServer();
        qbittorrent.Arm(harness.QBittorrentHttp);
        qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/info"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[HoldingsLimitBytes + 1]) }
            : null;
        var settings = new IntegrationConnectionSettings(
            Guid.Parse("5b8d2f4a-1c3e-4a6b-9d7f-0e2c4a6b8d1f"),
            FakeQBittorrentServer.DefaultBaseUrl,
            FakeQBittorrentServer.DefaultUserName,
            FakeQBittorrentServer.DefaultPassword,
            null);

        //Act
        var result = await harness.Adapter<IDownloadClient>(QBittorrentTypeId).ListJobsWithFilesAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "qBittorrent's list of every torrent with its files is larger than the app reads (4 MiB), so the app couldn't tell which torrents hold these copies.",
            TestTexts.En(result.Error));
    }

    [Fact]
    // The same limit through Transmission's adapter, which names itself in the shared sentence.
    public async Task TransmissionAdapter_holdings_too_large_sentence_writes_the_limit_in_MiB_as_its_format_did()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var transmission = new FakeTransmissionServer();
        transmission.Arm(harness.TransmissionHttp);
        transmission.Intercept = (_, call) => call?.Method == "torrent-get" && call.SessionId == transmission.SessionId
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[HoldingsLimitBytes + 1]) }
            : null;
        var settings = new IntegrationConnectionSettings(
            Guid.Parse("6c9e3a5b-2d4f-4b7c-8e0a-1f3d5b7c9e2a"),
            FakeTransmissionServer.DefaultBaseUrl,
            FakeTransmissionServer.DefaultUserName,
            FakeTransmissionServer.DefaultPassword,
            null);

        //Act
        var result = await harness.Adapter<IDownloadClient>(TransmissionTypeId).ListJobsWithFilesAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "Transmission's list of every torrent with its files is larger than the app reads (4 MiB), so the app couldn't tell which torrents hold these copies.",
            TestTexts.En(result.Error));
    }
}
