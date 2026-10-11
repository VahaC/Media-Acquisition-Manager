using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Tests.Files;

// MAM-26, ADR 0030 — where a replaced copy waits for the owner: .mam-quarantine at the top of the allowed root that holds
// it, a folder per replacement job, and below it the copy's path inside that root, so Restore knows where each name came
// from and two replacements of one name never meet. Where an allowed root is itself a library folder, the quarantine is
// inside it. The folder is told by its name at any depth — what the folder scan leaves out —, its letter case following
// the path's own style. A path that isn't a file inside the root, or already lies in a quarantine, has no place there.
// The paths are spelled out here rather than built by the rule.
public sealed class QuarantineFolderTests
{
    private static readonly Guid JobId = Guid.Parse("0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77");

    [Fact]
    public void Name_is_the_dot_mam_quarantine_folder()
    {
        //Arrange

        //Act
        var name = QuarantineFolder.Name;

        //Assert
        Assert.Equal(".mam-quarantine", name);
    }

    [Theory]
    [InlineData(".mam-quarantine", true)]
    [InlineData(".MAM-Quarantine", false)]
    [InlineData("mam-quarantine", false)]
    [InlineData(".mam-quarantine2", false)]
    [InlineData(".mam-quarantine ", false)]
    public void IsQuarantineName_on_a_posix_path_takes_the_name_in_its_own_letter_case_only(string name, bool expected)
    {
        //Arrange

        //Act
        var isQuarantine = QuarantineFolder.IsQuarantineName(name, PathStyle.Posix);

        //Assert
        // ext4 compares names byte for byte: .MAM-Quarantine is another folder there.
        Assert.Equal(expected, isQuarantine);
    }

    [Theory]
    [InlineData(".mam-quarantine", true)]
    [InlineData(".MAM-Quarantine", true)]
    [InlineData("mam-quarantine", false)]
    [InlineData(".mam-quarantine2", false)]
    public void IsQuarantineName_on_a_windows_path_ignores_letter_case(string name, bool expected)
    {
        //Arrange

        //Act
        var isQuarantine = QuarantineFolder.IsQuarantineName(name, PathStyle.Windows);

        //Assert
        // NTFS and SMB take any letter case for the same folder.
        Assert.Equal(expected, isQuarantine);
    }

    [Theory]
    [InlineData("/media/.mam-quarantine", true)]
    [InlineData("/media/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/movies/Alien (1979)/Alien.mkv", true)]
    [InlineData("/media/movies/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/Alien (1979)/Alien.mkv", true)]
    [InlineData("/media/movies/Alien (1979)/.mam-quarantine", true)]
    [InlineData("/.mam-quarantine/x.mkv", true)]
    [InlineData("/media/movies/Alien (1979)/Alien.mkv", false)]
    [InlineData("/media/.mam-quarantine-old/Alien.mkv", false)]
    [InlineData("/media/.MAM-Quarantine/Alien.mkv", false)]
    [InlineData("/media/movies/.mam-quarantine.mkv", false)]
    [InlineData(@"D:\media\.MAM-Quarantine\Alien.mkv", true)]
    [InlineData(@"D:\media\movies\Alien.mkv", false)]
    public void Holds_a_quarantine_folder_and_anything_inside_one_at_any_depth(string path, bool expected)
    {
        //Arrange
        var parsed = PathOf(path);

        //Act
        var holds = QuarantineFolder.Holds(parsed);

        //Assert
        // Any name on the way counts, the last one too, as a whole name only: .mam-quarantine.mkv is another name.
        Assert.Equal(expected, holds);
    }

    [Theory]
    [InlineData("/media", "/media/.mam-quarantine")]
    [InlineData("/media/movies", "/media/movies/.mam-quarantine")]
    [InlineData("/", "/.mam-quarantine")]
    [InlineData(@"D:\media", @"D:\media\.mam-quarantine")]
    public void Of_is_the_quarantine_at_the_top_of_the_allowed_root(string root, string expected)
    {
        //Arrange
        var parsed = PathOf(root);

        //Act
        var quarantine = QuarantineFolder.Of(parsed);

        //Assert
        Assert.Equal(expected, quarantine.ToString());
    }

    [Fact]
    public void PathOf_keeps_the_copys_path_inside_the_root_under_the_jobs_own_folder()
    {
        //Arrange
        var root = PathOf("/media");
        var copy = PathOf("/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv");

        //Act
        var quarantined = QuarantineFolder.PathOf(root, JobId, copy);

        //Assert
        Assert.Equal(
            "/media/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv", quarantined.ToString());
    }

    [Fact]
    public void PathOf_in_an_allowed_root_that_is_a_library_folder_lies_inside_that_folder()
    {
        //Arrange
        var root = PathOf("/media/movies");
        var copy = PathOf("/media/movies/Alien (1979)/Alien.mkv");

        //Act
        var quarantined = QuarantineFolder.PathOf(root, JobId, copy);

        //Assert
        // ADR 0030's one departure from ADR 0003: the quarantine sits in the library's location, where Jellyfin skips it
        // for its dot.
        Assert.Equal("/media/movies/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/Alien (1979)/Alien.mkv", quarantined.ToString());
    }

    [Fact]
    public void PathOf_two_jobs_of_one_name_are_two_places()
    {
        //Arrange
        var root = PathOf("/media");
        var copy = PathOf("/media/movies/Alien.mkv");
        var otherJobId = Guid.Parse("5a1d0c3e-2b4f-4e6a-8c7d-9e0f1a2b3c4d");

        //Act
        var first = QuarantineFolder.PathOf(root, JobId, copy);
        var second = QuarantineFolder.PathOf(root, otherJobId, copy);

        //Assert
        Assert.Equal("/media/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/movies/Alien.mkv", first.ToString());
        Assert.Equal("/media/.mam-quarantine/5a1d0c3e-2b4f-4e6a-8c7d-9e0f1a2b3c4d/movies/Alien.mkv", second.ToString());
    }

    [Fact]
    public void PathOf_on_a_windows_root_keeps_its_style()
    {
        //Arrange
        var root = PathOf(@"D:\media");
        var copy = PathOf(@"d:\media\movies\Alien.mkv");

        //Act
        var quarantined = QuarantineFolder.PathOf(root, JobId, copy);

        //Assert
        Assert.Equal(@"D:\media\.mam-quarantine\0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77\movies\Alien.mkv", quarantined.ToString());
    }

    [Theory]
    // The root itself: no file inside it.
    [InlineData("/media", "/media")]
    // Outside the root, or beside it with a longer name.
    [InlineData("/media", "/downloads/Alien.mkv")]
    [InlineData("/media", "/media2/Alien.mkv")]
    // Already in a quarantine: never a quarantine inside a quarantine.
    [InlineData("/media", "/media/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/movies/Alien.mkv")]
    [InlineData("/media", "/media/movies/.mam-quarantine/Alien.mkv")]
    public void PathOf_refuses_a_path_that_is_no_file_inside_the_root_or_already_quarantined(string root, string path)
    {
        //Arrange
        var parsedRoot = PathOf(root);
        var parsedPath = PathOf(path);

        //Act
        var exception = Record.Exception(() => QuarantineFolder.PathOf(parsedRoot, JobId, parsedPath));

        //Assert
        Assert.IsType<ArgumentException>(exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static NormalizedPath PathOf(string text)
    {
        Assert.True(NormalizedPath.TryParse(text, out var path, out _), $"“{text}” doesn't parse.");
        return path;
    }
}
