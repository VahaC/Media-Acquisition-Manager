using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-9 — reading a file's streams: the checks of Describe first, ffprobe on the file they opened, the open
// file asked again afterwards, and every way the read can fail turned into a status and a sentence. Stub
// executables stand in for ffprobe (FfprobeStubs); the real one runs where it is installed.
public sealed class LocalFileInventoryInspectionTests : IDisposable
{
    private static readonly TimeSpan GenerousLimit = TimeSpan.FromSeconds(30);

    private readonly TemporaryFolder _folder = new("mam-file-inspection-");
    private readonly string _media;
    private readonly string _stubs;

    public LocalFileInventoryInspectionTests()
    {
        _media = _folder.CreateFolder("media");
        _stubs = _folder.CreateFolder("stubs");
    }

    /// <summary>For <c>SkipUnless</c>: the tests of the real ffprobe need it on the PATH.</summary>
    public static bool HasFfprobe => InstalledFfprobe.FullPath is not null;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task InspectMedia_returns_the_streams_with_the_facts_of_the_file_they_were_read_from()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var inventory = InventoryWith(FfprobeStubs.Printing(_stubs, FfprobeSamples.ContinentalPath));

        //Act
        var result = await inventory.InspectMediaAsync(movie, [_media], TestContext.Current.CancellationToken);
        var described = await inventory.DescribeAsync(movie, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(described.Value, result.Value!.File);
        Assert.Equal(5_179_168, result.Value.Media.Duration!.Value.TotalMilliseconds);
        Assert.Equal(9, result.Value.Media.Tracks.Count);
    }

    [Fact]
    public async Task InspectMedia_hands_ffprobe_the_file_the_checks_opened_behind_a_folder_link()
    {
        //Arrange
        _folder.WriteFile("12345", "media", "real", "movie.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "linked"), Path.Combine(_media, "real"));
        var record = _folder.PathOf("arguments.txt");
        var inventory = InventoryWith(FfprobeStubs.Recording(_stubs, record, FfprobeSamples.ContinentalPath));

        //Act
        var result = await inventory.InspectMediaAsync(Path.Combine(_media, "linked", "movie.mkv"), [_media], TestContext.Current.CancellationToken);
        var arguments = FfprobeStubs.RecordedArguments(record);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        // ffprobe follows no link (ADR 0009). On Linux it reads the very file the checks opened, a descriptor
        // it inherits, so a link swapped in at the path since can't lead it elsewhere; on Windows the link-free
        // path, extended-length so Win32 can't reinterpret a name on the way (SystemPath), while the open
        // handle keeps every name on it from being renamed.
        if (OperatingSystem.IsWindows())
            Assert.Equal(@"file:\\?\" + Path.Combine(_media, "real", "movie.mkv"), arguments[^1]);
        else
            Assert.Matches(@"^file:/proc/self/fd/[0-9]+$", arguments[^1]);
    }

    [Fact]
    public async Task InspectMedia_gives_ffprobe_the_bytes_of_the_file_behind_the_link()
    {
        //Arrange
        _folder.WriteFile("the real movie", "media", "real", "movie.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "linked"), Path.Combine(_media, "real"));
        var copy = _folder.PathOf("what-ffprobe-read.bin");
        var inventory = InventoryWith(FfprobeStubs.Copying(_stubs, copy, FfprobeSamples.ContinentalPath));

        //Act
        var result = await inventory.InspectMediaAsync(Path.Combine(_media, "linked", "movie.mkv"), [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal("the real movie", await File.ReadAllTextAsync(copy, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InspectMedia_of_a_file_that_grows_while_ffprobe_reads_it_is_changed_while_reading()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var inventory = InventoryWith(FfprobeStubs.Appending(_stubs, FfprobeSamples.ContinentalPath));

        //Act
        var result = await inventory.InspectMediaAsync(movie, [_media], TestContext.Current.CancellationToken);

        //Assert
        // What ffprobe read belongs to neither version, so nothing is kept under either key.
        Assert.Equal(FileInventoryStatus.ChangedWhileReading, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(
            $"“{movie}” changed while ffprobe was reading it, so what was read can't be said to belong to either version.",
            TestTexts.En(result.Error));
        Assert.True(new FileInfo(movie).Length > 5, "The stub didn't append to the file.");
    }

    [Fact]
    public async Task InspectMedia_when_ffprobe_fails_is_media_unreadable_quoting_what_it_said()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var inventory = InventoryWith(FfprobeStubs.Failing(_stubs, 1, "Invalid data found when processing input"));

        //Act
        var result = await inventory.InspectMediaAsync(movie, [_media], TestContext.Current.CancellationToken);

        //Assert
        // ffprobe's verdict, without the name it was given: the sentence quotes the path as the app knows it.
        Assert.Equal(FileInventoryStatus.MediaUnreadable, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"ffprobe couldn't read “{movie}” as media: it said “Invalid data found when processing input”.", TestTexts.En(result.Error));
    }

    [Fact(Timeout = 60_000)]
    public async Task InspectMedia_when_ffprobe_runs_out_of_time_is_media_unreadable()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var inventory = new LocalFileInventory(
            new FfprobeProcess(FfprobeStubs.Hanging(_stubs), TimeSpan.FromSeconds(1), FfprobeProcess.MaximumOutputBytes));

        //Act
        var result = await inventory.InspectMediaAsync(movie, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.MediaUnreadable, result.Status);
        Assert.Equal($"ffprobe couldn't read “{movie}” as media: it didn't finish within 1 second.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task InspectMedia_when_ffprobe_prints_something_else_than_its_json_is_media_unreadable()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var notJson = _folder.WriteFile("this is not ffprobe's answer", "not-json.txt");
        var inventory = InventoryWith(FfprobeStubs.Printing(_stubs, notJson));

        //Act
        var result = await inventory.InspectMediaAsync(movie, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.MediaUnreadable, result.Status);
        Assert.Equal($"ffprobe couldn't read “{movie}” as media: its answer wasn't the JSON ffprobe writes.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task InspectMedia_without_ffprobe_is_inspector_unavailable_and_says_how_to_get_it()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var inventory = InventoryWith(_folder.PathOf("no-such-ffprobe"));

        //Act
        var result = await inventory.InspectMediaAsync(movie, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.InspectorUnavailable, result.Status);
        Assert.Null(result.Value);
        Assert.StartsWith("The app couldn't start ffprobe (", TestTexts.En(result.Error), StringComparison.Ordinal);
        Assert.EndsWith(
            "); the Docker image includes it, and elsewhere FFmpeg must be installed with ffprobe on the PATH.",
            TestTexts.En(result.Error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectMedia_outside_the_roots_never_starts_ffprobe()
    {
        //Arrange
        var secret = _folder.WriteFile("secret", "outside", "secret.txt");
        var record = _folder.PathOf("arguments.txt");
        var inventory = InventoryWith(FfprobeStubs.Recording(_stubs, record, FfprobeSamples.ContinentalPath));

        //Act
        var result = await inventory.InspectMediaAsync(secret, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.False(File.Exists(record), "ffprobe ran for a file outside the allowed roots.");
    }

    [Fact]
    public async Task InspectMedia_through_a_link_leading_out_never_starts_ffprobe()
    {
        //Arrange
        _folder.WriteFile("secret", "outside", "secret.txt");
        FileLinks.CreateFolderLink(Path.Combine(_media, "escape"), _folder.PathOf("outside"));
        var record = _folder.PathOf("arguments.txt");
        var inventory = InventoryWith(FfprobeStubs.Recording(_stubs, record, FfprobeSamples.ContinentalPath));

        //Act
        var result = await inventory.InspectMediaAsync(Path.Combine(_media, "escape", "secret.txt"), [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.False(File.Exists(record), "ffprobe ran through a link leading out of the allowed roots.");
    }

    [Fact]
    public async Task InspectMedia_of_a_missing_file_never_starts_ffprobe()
    {
        //Arrange
        var record = _folder.PathOf("arguments.txt");
        var inventory = InventoryWith(FfprobeStubs.Recording(_stubs, record, FfprobeSamples.ContinentalPath));
        var missing = Path.Combine(_media, "missing.mkv");

        //Act
        var result = await inventory.InspectMediaAsync(missing, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.NotFound, result.Status);
        Assert.Equal($"There is no file at “{missing}”.", TestTexts.En(result.Error));
        Assert.False(File.Exists(record));
    }

    [Fact(Skip = "ffprobe isn't on the PATH here (CI has no FFmpeg).", SkipUnless = nameof(HasFfprobe))]
    public async Task InspectMedia_with_the_real_ffprobe_reads_a_wave_file()
    {
        //Arrange
        var wave = _folder.PathOf("media", "silence.wav");
        WaveFile.WriteSilence(wave, seconds: 2);
        var inventory = new LocalFileInventory(new FfprobeProcess());

        //Act
        var result = await inventory.InspectMediaAsync(wave, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        var media = result.Value!.Media;
        Assert.Equal(TimeSpan.FromSeconds(2), media.Duration);
        // The container's overall rate, the header included: (44 + 32 000 bytes) × 8 / 2 s.
        Assert.Equal(128_176, media.BitRate);
        var track = Assert.Single(media.Tracks);
        Assert.Equal(
            (MediaTrackKind.Audio, "pcm_s16le", 1, (string?)null, (int?)null, (DynamicRange?)null),
            (track.Kind, track.Codec, track.Channels, track.Language, track.Width, track.DynamicRange));
    }

    [Fact(Skip = "ffprobe isn't on the PATH here (CI has no FFmpeg).", SkipUnless = nameof(HasFfprobe))]
    public async Task InspectMedia_with_the_real_ffprobe_finds_a_text_file_media_unreadable()
    {
        //Arrange
        var notes = _folder.WriteFile("Just notes, no media here.", "media", "notes.txt");
        var inventory = new LocalFileInventory(new FfprobeProcess());

        //Act
        var result = await inventory.InspectMediaAsync(notes, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.MediaUnreadable, result.Status);
        Assert.Equal($"ffprobe couldn't read “{notes}” as media: it said “Invalid data found when processing input”.", TestTexts.En(result.Error));
    }

    private static LocalFileInventory InventoryWith(string executable) =>
        new(new FfprobeProcess(executable, GenerousLimit, FfprobeProcess.MaximumOutputBytes));
}
