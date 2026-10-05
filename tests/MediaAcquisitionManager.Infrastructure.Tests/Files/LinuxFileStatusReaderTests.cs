using System.ComponentModel;
using System.Runtime.InteropServices;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-9 — the Linux reader between the checks and ffprobe (ADR 0009): a pipe or a device is refused without
// being opened, so it can't hang a run; a folder or a file swapped for a link after LinkResolver walked the
// path is refused rather than followed; and what ffprobe reads is the file the reader opened, whatever the
// path leads to by then. Linux only: this is where the app runs, and where CI runs the tests.
public sealed class LinuxFileStatusReaderTests : IDisposable
{
    private const string LinuxOnly = "The Linux reader runs on Linux only; CI runs these tests there.";

    private readonly TemporaryFolder _folder = new("mam-linux-reader-");
    private readonly string _media;

    public LinuxFileStatusReaderTests() => _media = _folder.CreateFolder("media");

    /// <summary>For <c>SkipUnless</c>.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    public void Dispose() => _folder.Dispose();

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux), Timeout = 10_000)]
    public async Task Describe_a_named_pipe_is_not_a_regular_file_and_is_never_opened()
    {
        //Arrange
        // Opened for reading, a pipe with no writer blocks until one comes: the look would hang the run.
        var pipe = _folder.PathOf("media", "movie.mkv");
        CreateNamedPipe(pipe);
        var inventory = new LocalFileInventory(new FfprobeProcess());

        //Act
        var result = await inventory.DescribeAsync(pipe, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.NotARegularFile, result.Status);
        Assert.Equal($"“{pipe}” is not a regular file but a device, a socket or a pipe.", result.Error);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux), Timeout = 10_000)]
    public async Task InspectMedia_of_a_named_pipe_never_starts_ffprobe()
    {
        //Arrange
        var pipe = _folder.PathOf("media", "movie.mkv");
        CreateNamedPipe(pipe);
        var record = _folder.PathOf("arguments.txt");
        var stubs = _folder.CreateFolder("stubs");
        var inventory = new LocalFileInventory(new FfprobeProcess(
            FfprobeStubs.Recording(stubs, record, FfprobeSamples.ContinentalPath), TimeSpan.FromSeconds(30), FfprobeProcess.MaximumOutputBytes));

        //Act
        var result = await inventory.InspectMediaAsync(pipe, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.NotARegularFile, result.Status);
        Assert.False(File.Exists(record), "ffprobe ran for a named pipe.");
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public void Open_through_a_folder_swapped_for_a_link_since_the_check_is_refused()
    {
        //Arrange
        // LinkResolver walked /media/movies/movie.mkv and found no link; then someone who may write in /media
        // put a link to a folder outside in place of "movies".
        _folder.WriteFile("secret", "outside", "movie.mkv");
        var path = _folder.PathOf("media", "movies", "movie.mkv");
        FileLinks.CreateFolderLink(_folder.PathOf("media", "movies"), _folder.PathOf("outside"));

        //Act
        var result = new LinuxFileStatusReader().Open(path, path);

        //Assert
        Assert.Null(result.Value);
        Assert.Equal(FileInventoryStatus.Unreadable, result.Status);
        Assert.Equal(
            $"“{path}” changed while the app was checking it: a folder on the way or the file itself became a link or something else, so the app didn't open it.",
            result.Error);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public void Open_of_a_file_swapped_for_a_link_since_the_check_is_refused()
    {
        //Arrange
        var outside = _folder.WriteFile("secret", "outside", "mam.db");
        var path = _folder.PathOf("media", "movie.mkv");
        Assert.True(FileLinks.TryCreateSymbolicLink(path, outside));

        //Act
        var result = new LinuxFileStatusReader().Open(path, path);

        //Assert
        Assert.Equal(FileInventoryStatus.Unreadable, result.Status);
        Assert.Null(result.Value);
        Assert.StartsWith($"“{path}” changed while the app was checking it", result.Error, StringComparison.Ordinal);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Open_gives_ffprobe_the_file_it_opened_even_after_its_path_leads_elsewhere()
    {
        //Arrange
        var path = _folder.WriteFile("the movie", "media", "movie.mkv");
        var outside = _folder.WriteFile("secret", "outside", "mam.db");
        using var opened = new LinuxFileStatusReader().Open(path, path).Value!;
        // Swapped after the open: the movie moved aside, a link to the app's data in its place.
        File.Move(path, _folder.PathOf("media", "aside.mkv"));
        Assert.True(FileLinks.TryCreateSymbolicLink(path, outside));

        //Act
        // What ffprobe opens: the descriptor it inherits, through /proc/self (here the test's own).
        var read = await File.ReadAllTextAsync(opened.ProbePath, TestContext.Current.CancellationToken);
        var again = opened.ReadFactsAgain(path);

        //Assert
        Assert.Equal("the movie", read);
        Assert.Equal(opened.Facts, again.Value);
    }

    // MAM-18 — the walk of the allowed roots opens each folder the same way (ADR 0021).

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public void OpenFolder_reports_the_folders_identity_the_same_each_time_it_is_opened()
    {
        //Arrange
        var movies = _folder.CreateFolder("media", "movies");
        var shows = _folder.CreateFolder("media", "shows");
        var reader = new LinuxFileStatusReader();

        //Act
        using var first = reader.OpenFolder(movies, movies).Value!;
        using var again = reader.OpenFolder(movies, movies).Value!;
        using var other = reader.OpenFolder(shows, shows).Value!;

        //Assert
        Assert.NotNull(first.DeviceId);
        Assert.NotNull(first.Inode);
        Assert.Equal((first.DeviceId, first.Inode), (again.DeviceId, again.Inode));
        Assert.Equal(first.DeviceId, other.DeviceId);
        Assert.NotEqual(first.Inode, other.Inode);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public void OpenFolder_through_a_folder_swapped_for_a_link_since_the_check_is_refused()
    {
        //Arrange
        // The walk listed /media and found the folder "movies"; then someone who may write in /media put a link to a
        // folder outside in its place.
        _folder.WriteFile("secret", "outside", "movie.mkv");
        var path = _folder.PathOf("media", "movies");
        FileLinks.CreateFolderLink(path, _folder.PathOf("outside"));

        //Act
        var result = new LinuxFileStatusReader().OpenFolder(path, path);

        //Assert
        Assert.Null(result.Value);
        Assert.Equal(FileInventoryStatus.Unreadable, result.Status);
        Assert.Equal(
            $"“{path}” changed while the app was checking it: a folder on the way or the file itself became a link or something else, so the app didn't open it.",
            result.Error);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public void OpenFolder_of_a_file_is_refused()
    {
        //Arrange
        var path = _folder.WriteFile("12345", "media", "movie.mkv");

        //Act
        var result = new LinuxFileStatusReader().OpenFolder(path, path);

        //Assert
        Assert.Null(result.Value);
        Assert.Equal(FileInventoryStatus.Unreadable, result.Status);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public void OpenFolder_lists_the_folder_it_opened_even_after_its_path_leads_elsewhere()
    {
        //Arrange
        _folder.WriteFile("the movie", "media", "movies", "movie.mkv");
        _folder.WriteFile("secret", "outside", "mam.db");
        var path = _folder.PathOf("media", "movies");
        using var opened = new LinuxFileStatusReader().OpenFolder(path, path).Value!;
        // Swapped after the open: the folder moved aside, a link to the app's data in its place.
        Directory.Move(path, _folder.PathOf("media", "aside"));
        FileLinks.CreateFolderLink(path, _folder.PathOf("outside"));

        //Act
        var names = new DirectoryInfo(opened.ListPath).EnumerateFileSystemInfos().Select(entry => entry.Name).ToList();

        //Assert
        Assert.Equal(["movie.mkv"], names);
    }

    private static void CreateNamedPipe(string path)
    {
        if (MakeFifo(path, Convert.ToUInt32("644", 8)) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
