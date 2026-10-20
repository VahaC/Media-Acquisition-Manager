using System.Diagnostics;
using System.Runtime.InteropServices;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-9 — the ffprobe process through stub executables (FfprobeStubs): the arguments it gets, both streams read
// at once, the exit code and the reason it gave, the time limit and the caller's cancellation killing the
// whole tree, and the output cap. CI has no FFmpeg; the stubs run everywhere, and the real ffprobe runs where
// it is installed.
public sealed class FfprobeProcessTests : IDisposable
{
    /// <summary>Far longer than any stub needs, far shorter than a hung test run.</summary>
    private static readonly TimeSpan GenerousLimit = TimeSpan.FromSeconds(30);

    /// <summary>A test that waits for a kill must end well before this; reaching it means the kill failed.</summary>
    private static readonly TimeSpan KillDeadline = TimeSpan.FromSeconds(20);

    private readonly TemporaryFolder _folder = new("mam-ffprobe-process-");
    private readonly string _movie;

    public FfprobeProcessTests() => _movie = _folder.WriteFile("12345", "media", "movie.mkv");

    /// <summary>For <c>SkipUnless</c>: the tests of the real ffprobe need it on the PATH.</summary>
    public static bool HasFfprobe => InstalledFfprobe.FullPath is not null;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Run_hands_ffprobe_its_options_and_the_file_as_separate_arguments()
    {
        //Arrange
        var record = _folder.PathOf("arguments.txt");
        var process = ProbeWith(FfprobeStubs.Recording(_folder.FullName, record, FfprobeSamples.ContinentalPath));

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);
        var arguments = FfprobeStubs.RecordedArguments(record);

        //Assert
        Assert.Equal(MediaProbeStatus.Succeeded, result.Status);
        // file: makes the name a file whatever it contains; -protocol_whitelist file keeps a crafted playlist
        // from opening the network (specification §4), and -format_whitelist keeps a crafted list from opening
        // other files (ADR 0009).
        Assert.Equal(
            [
                "-v", "error", "-protocol_whitelist", "file", "-format_whitelist", "matroska,mov,avi,mpegts,mpeg,asf,flv,ogg,rm,wav",
                "-print_format", "json", "-show_format", "-show_streams", "file:" + _movie,
            ],
            arguments);
    }

    [Fact]
    public async Task Run_returns_what_ffprobe_printed()
    {
        //Arrange
        var process = ProbeWith(FfprobeStubs.Printing(_folder.FullName, FfprobeSamples.ContinentalPath));

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MediaProbeStatus.Succeeded, result.Status);
        Assert.Null(result.Reason);
        Assert.Equal(FfprobeSamples.ContinentalText(), result.Output);
        Assert.True(FfprobeOutputParser.TryParse(result.Output!, out var facts));
        Assert.Equal(9, facts.Tracks.Count);
    }

    [Fact]
    public async Task Run_that_fails_quotes_ffprobes_verdict_from_its_last_line_without_the_file_name()
    {
        //Arrange
        var process = ProbeWith(FfprobeStubs.Failing(_folder.FullName, 1, "I/O error"));

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MediaProbeStatus.Failed, result.Status);
        Assert.Null(result.Output);
        // The stub writes what real ffprobe does for a cut-off file: an empty line, a demuxer's noise, then
        // "file:<path>: I/O error". The verdict is the reason; the sentence names the file itself.
        Assert.Equal("it said “I/O error”", TestTexts.En(result.Reason));
    }

    [Fact]
    public async Task Run_that_fails_on_a_format_left_out_of_the_whitelist_names_the_format()
    {
        //Arrange
        // What ffprobe 8.0.1 writes for a file named .mkv whose content is an ffconcat list.
        var process = ProbeWith(FfprobeStubs.Failing(
            _folder.FullName, 1, "Invalid argument", noise: "[concat @ 000001b875236380] Format not on whitelist 'matroska,mov'"));

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        // Not the bare "Invalid argument": the owner learns what the file really is.
        Assert.Equal(MediaProbeStatus.Failed, result.Status);
        Assert.Equal("its content is “concat”, which isn't a media container the app reads", TestTexts.En(result.Reason));
    }

    [Fact]
    public async Task Run_that_fails_without_a_verdict_quotes_the_first_line_that_says_anything()
    {
        //Arrange
        var process = ProbeWith(FfprobeStubs.Failing(_folder.FullName, 1, verdict: null, noise: "Something went wrong"));

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        // The stub writes an empty line first: the first line that says anything.
        Assert.Equal((MediaProbeStatus.Failed, "it said “Something went wrong”"), (result.Status, TestTexts.En(result.Reason)));
    }

    [Fact]
    public async Task Run_that_fails_without_a_word_names_the_exit_code()
    {
        //Arrange
        var process = ProbeWith(FfprobeStubs.Failing(_folder.FullName, 3, verdict: null, noise: null));

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MediaProbeStatus.Failed, result.Status);
        Assert.Equal("it stopped with exit code 3", TestTexts.En(result.Reason));
    }

    [Fact(Timeout = 60_000)]
    public async Task Run_that_never_finishes_is_killed_at_the_time_limit()
    {
        //Arrange
        var process = new FfprobeProcess(FfprobeStubs.Hanging(_folder.FullName), TimeSpan.FromSeconds(1), FfprobeProcess.MaximumOutputBytes);
        var stopwatch = Stopwatch.StartNew();

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MediaProbeStatus.Failed, result.Status);
        Assert.Equal("it didn't finish within 1 second", TestTexts.En(result.Reason));
        // Returning at all proves the stub's child was killed too: it held the pipes open for ten minutes.
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1), KillDeadline);
    }

    [Fact(Timeout = 60_000)]
    public async Task Run_cancelled_by_the_caller_kills_ffprobe_and_throws()
    {
        //Arrange
        var process = ProbeWith(FfprobeStubs.Hanging(_folder.FullName));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        //Act
        var exception = await Record.ExceptionAsync(() => process.RunAsync(_movie, cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(stopwatch.Elapsed < KillDeadline, $"The run took {stopwatch.Elapsed} to end after the cancellation.");
    }

    [Fact]
    public async Task Run_with_no_executable_is_unavailable_in_the_systems_words()
    {
        //Arrange
        var process = ProbeWith(_folder.PathOf("no-such-ffprobe"));

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MediaProbeStatus.Unavailable, result.Status);
        // ENOENT and ERROR_FILE_NOT_FOUND are both 2.
        Assert.Equal(Marshal.GetPInvokeErrorMessage(2), result.StartError);
    }

    [Fact]
    public async Task Run_printing_more_than_the_cap_is_failed()
    {
        //Arrange
        // The Continental's answer is about 22 KB.
        var process = new FfprobeProcess(FfprobeStubs.Printing(_folder.FullName, FfprobeSamples.ContinentalPath), GenerousLimit, 4096);

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MediaProbeStatus.Failed, result.Status);
        Assert.Null(result.Output);
        Assert.Equal("it printed more than 4 KiB", TestTexts.En(result.Reason));
    }

    [Fact(Timeout = 60_000)]
    public async Task Run_of_an_ffprobe_that_writes_a_megabyte_of_errors_before_its_answer_succeeds()
    {
        //Arrange
        // A pipe holds about 64 KB: read one stream after the other, and the stub would block on the first
        // until the time limit.
        var errors = _folder.WriteFile(new string('x', 1024 * 1024), "errors.txt");
        var process = ProbeWith(FfprobeStubs.Chatty(_folder.FullName, errors, FfprobeSamples.ContinentalPath));

        //Act
        var result = await process.RunAsync(_movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MediaProbeStatus.Succeeded, result.Status);
        Assert.Equal(FfprobeSamples.ContinentalText(), result.Output);
    }

    [Fact(Skip = "ffprobe isn't on the PATH here (CI has no FFmpeg).", SkipUnless = nameof(HasFfprobe))]
    public async Task Run_of_the_real_ffprobe_refuses_a_file_whose_content_is_a_list_of_other_files()
    {
        //Arrange
        // Named .mkv, but an ffconcat list naming a file beside it: read as a list, ffprobe would open that
        // file instead — and through a list or a playlist, any file, outside the allowed roots too.
        var other = _folder.PathOf("media", "other.wav");
        WaveFile.WriteSilence(other, seconds: 1);
        var list = _folder.WriteFile("ffconcat version 1.0\nfile other.wav\n", "media", "list.mkv");
        var process = ProbeWith(InstalledFfprobe.FullPath!);

        //Act
        var result = await process.RunAsync(list, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MediaProbeStatus.Failed, result.Status);
        Assert.Null(result.Output);
        Assert.Equal("its content is “concat”, which isn't a media container the app reads", TestTexts.En(result.Reason));
    }

    [Fact]
    public void Defaults_are_ffprobe_from_the_path_two_minutes_and_8_mib()
    {
        //Arrange
        // Pinned: each number is justified where it is declared, and a change should be deliberate.

        //Act
        var defaults = (FfprobeProcess.DefaultExecutable, FfprobeProcess.DefaultTimeLimit, FfprobeProcess.MaximumOutputBytes, FfprobeProcess.MaximumErrorBytes);

        //Assert
        Assert.Equal(("ffprobe", TimeSpan.FromMinutes(2), 8 * 1024 * 1024, 64 * 1024), defaults);
    }

    private static FfprobeProcess ProbeWith(string executable) =>
        new(executable, GenerousLimit, FfprobeProcess.MaximumOutputBytes);
}
