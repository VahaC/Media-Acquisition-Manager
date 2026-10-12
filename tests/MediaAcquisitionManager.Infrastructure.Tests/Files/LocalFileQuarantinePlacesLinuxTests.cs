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
// MountTableTests. MAM-39 — a folder the app's user may not write to is named with its owner's ids and the process's
// effective ids, each read here another way (coreutils' stat, /proc/self/status): the folder itself when it is the one
// refused, the quarantine's side otherwise — .mam-quarantine once it is there, the root while it isn't —; no folder
// that is no bar is named. Refusals need a user the permissions bind, so these skip as root. Linux only: this is where
// the app runs, and where CI runs the tests.
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
        Assert.All(places, place => Assert.Null(place.WriteAccess));
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
        var place = Assert.Single(places);
        Assert.Equal([RenamePlaceProblem.ReadOnly], place.Problems);
        Assert.Null(place.WriteAccess);
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
    public async Task DescribeRenamePlaces_of_a_folder_the_app_may_not_write_to_is_not_writable_naming_it_its_owner_and_the_apps_ids()
    {
        //Arrange
        var movies = _folder.CreateFolder("media", "movies");
        File.SetUnixFileMode(movies, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        _locked.Add(movies);
        if (CanWrite(movies))
            Assert.Skip("This process writes into a folder with mode 0500 anyway (it runs as root), so it can't see the refusal.");
        var owner = LinuxProbes.OwnerOf(movies);
        var app = LinuxProbes.EffectiveIds();

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        var place = Assert.Single(places);
        Assert.Equal([RenamePlaceProblem.NotWritable], place.Problems);
        Assert.Equal(new RenamePlaceWriteAccess(movies, owner.UserId, owner.GroupId, app.UserId, app.GroupId), place.WriteAccess);
    }

    [Theory(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [InlineData("/var/mail")]
    [InlineData("/var/local")]
    [UnsupportedOSPlatform("windows")]
    public async Task DescribeRenamePlaces_of_a_folder_another_user_owns_names_that_users_ids_beside_the_apps(string folder)
    {
        //Arrange
        // The case the page is for: a media folder that belongs to someone else than the app's user. A test can't give a
        // folder away without root, so it takes one the system already gave away, under /var: /var/mail, root:mail (0:8),
        // and /var/local, root:staff (0:50), on Debian and Ubuntu. Their group differs from their owner, so an owner's
        // user and group read the wrong way round can't pass, and from this process's group, so the app's can't stand in
        // for them. The owner is asked first, so a run as root skips before it would probe a system folder by writing to
        // it.
        const string Root = "/var";
        if (!Directory.Exists(folder))
            Assert.Skip($"This system has no {folder}, so it has no other user's folder there.");
        var owner = LinuxProbes.OwnerOf(folder);
        var app = LinuxProbes.EffectiveIds();
        if (owner.UserId == app.UserId)
            Assert.Skip($"{folder} belongs to this process's own user here (it runs as root), so another user's ids can't be shown.");
        if (owner.GroupId == owner.UserId || owner.GroupId == app.GroupId)
            Assert.Skip($"{folder}'s group is {owner.GroupId} here, the same as its owner or this process's group, so a mix-up couldn't be seen.");
        try
        {
            if (CanWrite(folder))
                Assert.Skip($"This process may write into {folder} all the same (its group, say), so it can't see the refusal.");
        }
        catch (IOException)
        {
            // EROFS: a read-only mount refuses before the folder's owner is asked.
            Assert.Skip($"{folder} is on a read-only mount here, so the refusal isn't its owner's.");
        }

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([folder], [Root], TestContext.Current.CancellationToken);

        //Assert
        // The folder itself, though /var refuses too: its owner is whom the app should run as.
        var place = Assert.Single(places);
        Assert.Contains(RenamePlaceProblem.NotWritable, place.Problems);
        Assert.Equal(new RenamePlaceWriteAccess(folder, owner.UserId, owner.GroupId, app.UserId, app.GroupId), place.WriteAccess);
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
        var place = Assert.Single(places);
        Assert.Empty(place.Problems);
        Assert.Null(place.WriteAccess);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task DescribeRenamePlaces_under_a_writable_root_whose_quarantine_the_app_may_not_write_to_is_not_writable_naming_the_quarantine()
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
        var owner = LinuxProbes.OwnerOf(quarantine);
        var app = LinuxProbes.EffectiveIds();

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        // As the page knows it: the allowed root's app path and the quarantine's name.
        var place = Assert.Single(places);
        Assert.Equal([RenamePlaceProblem.NotWritable], place.Problems);
        Assert.Equal(
            new RenamePlaceWriteAccess(_media + "/.mam-quarantine", owner.UserId, owner.GroupId, app.UserId, app.GroupId),
            place.WriteAccess);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task DescribeRenamePlaces_under_a_root_the_app_may_not_write_to_with_no_quarantine_yet_is_not_writable_naming_the_root()
    {
        //Arrange
        // No .mam-quarantine yet: the first move would make it in /media, which the app's user may not write to.
        var movies = _folder.CreateFolder("media", "movies");
        File.SetUnixFileMode(_media, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        _locked.Add(_media);
        if (CanWrite(_media))
            Assert.Skip("This process writes into a folder with mode 0555 anyway (it runs as root), so it can't see the refusal.");
        var owner = LinuxProbes.OwnerOf(_media);
        var app = LinuxProbes.EffectiveIds();

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        var place = Assert.Single(places);
        Assert.Equal([RenamePlaceProblem.NotWritable], place.Problems);
        Assert.Equal(new RenamePlaceWriteAccess(_media, owner.UserId, owner.GroupId, app.UserId, app.GroupId), place.WriteAccess);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task DescribeRenamePlaces_where_neither_the_folder_nor_its_root_is_writable_names_the_folder()
    {
        //Arrange
        // Both refuse: the ticked folder is named, as its owner is whom the app should run as.
        var movies = _folder.CreateFolder("media", "movies");
        File.SetUnixFileMode(movies, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        File.SetUnixFileMode(_media, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        _locked.Add(movies);
        _locked.Add(_media);
        if (CanWrite(movies))
            Assert.Skip("This process writes into a folder with mode 0500 anyway (it runs as root), so it can't see the refusal.");
        var owner = LinuxProbes.OwnerOf(movies);
        var app = LinuxProbes.EffectiveIds();

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies], [_media], TestContext.Current.CancellationToken);

        //Assert
        var place = Assert.Single(places);
        Assert.Equal([RenamePlaceProblem.NotWritable], place.Problems);
        Assert.Equal(new RenamePlaceWriteAccess(movies, owner.UserId, owner.GroupId, app.UserId, app.GroupId), place.WriteAccess);
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
        var place = Assert.Single(places);
        Assert.Equal([RenamePlaceProblem.CouldNotCheck], place.Problems);
        Assert.Null(place.WriteAccess);
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
            (expected, actual) => expected.FolderAppPath == actual.FolderAppPath
                && expected.Problems.SequenceEqual(actual.Problems)
                && expected.WriteAccess == actual.WriteAccess);
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
