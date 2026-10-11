using System.Globalization;
using System.Runtime.Versioning;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-26 — the Media folders page's hint of whether a rename into the quarantine can work in a ticked folder (ADR
// 0033), over real folders and the real mount table: a writable folder on its root's mount has no problem; a folder on
// another mount, one on a mount the table says is read-only, one the app's user may not write to, and one the app can't
// look at each say so; one answer per folder, in the order asked. The write permission asked of the quarantine's side is
// .mam-quarantine's once it is there, the root's only while it isn't. A read-only bind mount needs root to make, so the
// mount table is pointed at a file of the test's own for that, with the real mount's id; the parsing itself is in
// MountTableTests. Linux only: this is where the app runs, and where CI runs the tests.
public sealed class LocalFileQuarantinePlacesLinuxTests : IDisposable
{
    private const string LinuxOnly = "The quarantine renames on Linux only; CI runs these tests there.";

    private readonly TemporaryFolder _folder = new("mam-quarantine-places-");
    private readonly LocalFileQuarantine _quarantine = new();
    private readonly List<string> _locked = [];
    private readonly List<string> _elsewhere = [];
    private readonly string _media;

    public LocalFileQuarantinePlacesLinuxTests() => _media = _folder.CreateFolder("media");

    /// <summary>For <c>SkipUnless</c>.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach (var folder in _locked)
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        foreach (var folder in _elsewhere)
            Directory.Delete(folder, recursive: true);

        _folder.Dispose();
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeRenamePlaces_of_writable_folders_on_their_roots_mount_has_no_problem_and_answers_in_the_order_asked()
    {
        //Arrange
        var movies = _folder.CreateFolder("media", "movies");
        var shows = _folder.CreateFolder("media", "tv-shows");

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([shows, movies, _media], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([shows, movies, _media], places.Select(place => place.FolderAppPath));
        Assert.All(places, place => Assert.Empty(place.Problems));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeRenamePlaces_of_a_folder_on_another_mount_than_its_root_is_other_mount()
    {
        //Arrange
        // /media/fast leads to a second allowed root on /dev/shm: the quarantine of /media is on another mount.
        if (!Directory.Exists("/dev/shm"))
            Assert.Skip("This system has no /dev/shm, so no second mount is at hand.");
        var elsewhere = Directory.CreateDirectory(Path.Combine("/dev/shm", $"mam-quarantine-places-{Guid.NewGuid():N}")).FullName;
        _elsewhere.Add(elsewhere);
        if (LinuxProbes.MountIdOf(elsewhere) == LinuxProbes.MountIdOf(_media))
            Assert.Skip("/dev/shm is on the temp folder's mount here, so no folder on another mount can be shown.");
        var link = _folder.PathOf("media", "fast");
        FileLinks.CreateFolderLink(link, elsewhere);

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([link], [_media, elsewhere], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([RenamePlaceProblem.OtherMount], Assert.Single(places).Problems);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeRenamePlaces_of_a_folder_whose_mount_the_table_says_is_read_only_is_read_only()
    {
        //Arrange
        // The mount's own options say ro, the file system's after the type rw: a read-only bind of a writable disk.
        var movies = _folder.CreateFolder("media", "movies");
        var mountId = LinuxProbes.MountIdOf(movies) ?? throw new InvalidOperationException("This kernel doesn't report mount ids.");
        var mountInfo = _folder.WriteFile(
            string.Create(CultureInfo.InvariantCulture, $"{mountId} 1 0:50 / / ro,relatime shared:1 - overlay overlay rw,lowerdir=/l\n"),
            "mountinfo");
        var quarantine = new LocalFileQuarantine { MountInfoPath = mountInfo };

        //Act
        var places = await quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([RenamePlaceProblem.ReadOnly], Assert.Single(places).Problems);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeRenamePlaces_where_the_mount_table_cant_be_read_could_not_check()
    {
        //Arrange
        var movies = _folder.CreateFolder("media", "movies");
        var quarantine = new LocalFileQuarantine { MountInfoPath = _folder.PathOf("no-such-mountinfo") };

        //Act
        var places = await quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([RenamePlaceProblem.CouldNotCheck], Assert.Single(places).Problems);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task DescribeRenamePlaces_of_a_folder_the_app_may_not_write_to_is_not_writable()
    {
        //Arrange
        var movies = _folder.CreateFolder("media", "movies");
        File.SetUnixFileMode(movies, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        _locked.Add(movies);
        if (CanWrite(movies))
            Assert.Skip("This process writes into a folder with mode 0500 anyway (it runs as root), so it can't see the refusal.");

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([RenamePlaceProblem.NotWritable], Assert.Single(places).Problems);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task DescribeRenamePlaces_under_a_root_the_app_may_not_write_to_whose_quarantine_it_may_is_writable()
    {
        //Arrange
        // A root-owned /media whose .mam-quarantine belongs to the app's user: a move makes nothing in /media itself, as
        // the folder it would make there is there already, so every move works.
        var movies = _folder.CreateFolder("media", "movies");
        _folder.CreateFolder("media", ".mam-quarantine");
        File.SetUnixFileMode(_media, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        _locked.Add(_media);
        if (CanWrite(_media))
            Assert.Skip("This process writes into a folder with mode 0555 anyway (it runs as root), so it can't see the difference.");

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Empty(Assert.Single(places).Problems);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task DescribeRenamePlaces_under_a_writable_root_whose_quarantine_the_app_may_not_write_to_is_not_writable()
    {
        //Arrange
        // A writable /media with a root-owned .mam-quarantine: every move would fail making its job's folder there.
        var movies = _folder.CreateFolder("media", "movies");
        var quarantine = _folder.CreateFolder("media", ".mam-quarantine");
        File.SetUnixFileMode(quarantine, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        _locked.Add(quarantine);
        if (CanWrite(quarantine))
            Assert.Skip("This process writes into a folder with mode 0555 anyway (it runs as root), so it can't see the refusal.");

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([RenamePlaceProblem.NotWritable], Assert.Single(places).Problems);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeRenamePlaces_under_a_root_whose_quarantine_is_a_link_could_not_check()
    {
        //Arrange
        // The move refuses a .mam-quarantine that is a link, so the hint can't promise anything there.
        var movies = _folder.CreateFolder("media", "movies");
        FileLinks.CreateFolderLink(_folder.PathOf("media", ".mam-quarantine"), _folder.CreateFolder("outside"));

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([RenamePlaceProblem.CouldNotCheck], Assert.Single(places).Problems);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeRenamePlaces_of_a_folder_it_cant_look_at_could_not_check_and_never_throws()
    {
        //Arrange
        // Gone, outside every allowed root, not a path at all.
        var gone = _folder.PathOf("media", "gone");
        var outside = _folder.CreateFolder("outside");

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([gone, outside, "movies"], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [
                new RenamePlace(gone, [RenamePlaceProblem.CouldNotCheck]),
                new RenamePlace(outside, [RenamePlaceProblem.CouldNotCheck]),
                new RenamePlace("movies", [RenamePlaceProblem.CouldNotCheck]),
            ],
            places,
            (expected, actual) => expected.FolderAppPath == actual.FolderAppPath && expected.Problems.SequenceEqual(actual.Problems));
    }

    private static bool CanWrite(string folder)
    {
        var probe = Path.Combine(folder, ".write-probe");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
