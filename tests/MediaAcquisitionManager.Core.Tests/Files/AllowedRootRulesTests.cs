using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Tests.Files;

// MAM-9 — what the owner may save as the allowed roots, the folders the app may read files in: any
// number up to 20 (none included, the default), each a full path of the style the app runs on that
// names a folder rather than the whole file system, and no folder listed twice or inside another. Every
// refusal is pinned with the sentence the Media folders page shows, for both styles through the explicit
// appStyle argument, so the suite proves the Windows rules on Linux CI and the Linux ones on Windows.
public sealed class AllowedRootRulesTests
{
    // Spelled out rather than read from AllowedRootRules: they are what the settings page shows.
    private const string TooManyError = "The app can have at most 20 media folders.";
    private const string BlankError = "Each media folder needs its path.";
    private const string TooLongError = "A media folder's path can be at most 1024 characters long.";
    private const string ControlCharacterError = "A media folder's path can't contain line breaks or other control characters.";
    private const string LinuxNotAFullPathError = "“{0}” is not a full path. Start it with /.";
    private const string WindowsNotAFullPathError = "“{0}” is not a full path. Start it with a drive letter such as D:\\.";
    private const string DotSegmentError = "“{0}” contains . or .. as a folder name. Write the folder's full path without them.";
    private const string WindowsPathOnLinuxError =
        "“{0}” is a Windows path, but this app runs on Linux. Give the folder's path inside the app, such as /media.";
    private const string LinuxPathOnWindowsError =
        "“{0}” is a Linux path, but this app runs on Windows. Give the folder's path inside the app, such as D:\\Media.";
    private const string LinuxWholeFileSystemError = "“{0}” is the whole file system. Name the folder the media is in, such as /media.";
    private const string WindowsWholeFileSystemError = "“{0}” is the whole file system. Name the folder the media is in, such as D:\\Media.";
    private const string ListedTwiceError = "“{0}” is listed twice. Keep one.";
    private const string InsideAnotherError = "“{0}” is inside “{1}”, which already allows it. Keep only “{1}”.";

    /// <summary>Lists whose first broken rule is not the only one they break, with the sentence of the
    /// one that must win: the count first, then each path's own rules in order, then a folder listed
    /// twice, then a folder inside another.</summary>
    public static TheoryData<string[], string> FirstFailures => new()
    {
        // 21 entries, every one blank: the count is read before any path.
        { [.. Enumerable.Repeat("", 21)], TooManyError },
        // A folder twice and a folder inside another, but a path after them is broken.
        { ["/media", "/media", "/media/movies", "relative"], string.Format(LinuxNotAFullPathError, "relative") },
        { ["/media", "/media/movies", "/"], string.Format(LinuxWholeFileSystemError, "/") },
        // Two broken paths: the earlier one is named.
        { ["media", "/media/../etc"], string.Format(LinuxNotAFullPathError, "media") },
        { ["/media/../etc", "media"], string.Format(DotSegmentError, "/media/../etc") },
        { [@"D:\Media", "/"], string.Format(WindowsPathOnLinuxError, @"D:\Media") },
        // A folder inside another comes first in the list, the folder listed twice after it: twice wins.
        { ["/data", "/data/movies", "/media", "/media/"], string.Format(ListedTwiceError, "/media") },
        // One path breaking two rules: the length is read before the characters, the characters before
        // the shape.
        { ["/a\n" + new string('a', 1024)], TooLongError },
        { ["me\ndia"], ControlCharacterError },
    };

    [Theory]
    [InlineData(PathStyle.Posix)]
    [InlineData(PathStyle.Windows)]
    public void Validate_accepts_no_paths(PathStyle appStyle)
    {
        //Arrange
        string?[] none = [];

        //Act
        var error = AllowedRootRules.Validate(none, appStyle);

        //Assert
        // The default: an installation that reads nothing until the owner names a folder.
        Assert.Null(error);
    }

    [Theory]
    [InlineData(PathStyle.Posix, "/media/{0}")]
    [InlineData(PathStyle.Windows, @"D:\Media\{0}")]
    public void Validate_accepts_20_paths(PathStyle appStyle, string pattern)
    {
        //Arrange
        var paths = Enumerable.Range(1, 20).Select(number => string.Format(pattern, number)).ToList();

        //Act
        var error = AllowedRootRules.Validate(paths, appStyle);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(PathStyle.Posix, "/media/{0}")]
    [InlineData(PathStyle.Windows, @"D:\Media\{0}")]
    public void Validate_with_more_than_20_paths_is_the_rule(PathStyle appStyle, string pattern)
    {
        //Arrange
        var paths = Enumerable.Range(1, 21).Select(number => string.Format(pattern, number)).ToList();

        //Act
        var error = AllowedRootRules.Validate(paths, appStyle);

        //Assert
        Assert.Equal(TooManyError, error);
    }

    [Theory]
    [InlineData("", PathStyle.Posix)]
    [InlineData("   ", PathStyle.Windows)]
    // Whitespace around a path is ignored, line breaks included, so nothing is left of this one.
    [InlineData("\t\r\n ", PathStyle.Posix)]
    [InlineData(null, PathStyle.Windows)]
    public void Validate_with_a_blank_path_is_the_rule(string? path, PathStyle appStyle)
    {
        //Arrange
        string?[] paths = [appStyle == PathStyle.Posix ? "/media" : @"D:\Media", path];

        //Act
        var error = AllowedRootRules.Validate(paths, appStyle);

        //Assert
        Assert.Equal(BlankError, error);
    }

    [Theory]
    [InlineData(PathStyle.Posix, "/")]
    [InlineData(PathStyle.Windows, @"D:\")]
    public void Validate_with_a_path_longer_than_1024_characters_is_the_rule(PathStyle appStyle, string root)
    {
        //Arrange
        var tooLong = root + new string('a', 1025 - root.Length);

        //Act
        var error = AllowedRootRules.Validate([tooLong], appStyle);

        //Assert
        Assert.Equal(TooLongError, error);
    }

    [Theory]
    [InlineData(PathStyle.Posix, "/")]
    [InlineData(PathStyle.Windows, @"D:\")]
    public void Validate_accepts_a_path_of_1024_characters_between_spaces(PathStyle appStyle, string root)
    {
        //Arrange
        var longest = root + new string('a', 1024 - root.Length);

        //Act
        // Counted as the stored path, without the spaces around it, which are not part of it.
        var error = AllowedRootRules.Validate(["  " + longest + "  "], appStyle);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("/me\ndia", PathStyle.Posix)]
    [InlineData("/media/\u0007", PathStyle.Posix)]
    [InlineData("/media\u007F", PathStyle.Posix)]
    [InlineData("D:\\Me\rdia", PathStyle.Windows)]
    [InlineData("D:\\Media\u001B", PathStyle.Windows)]
    public void Validate_with_a_control_character_is_the_rule(string path, PathStyle appStyle)
    {
        //Arrange
        string?[] paths = [path];

        //Act
        var error = AllowedRootRules.Validate(paths, appStyle);

        //Assert
        Assert.Equal(ControlCharacterError, error);
    }

    [Theory]
    [InlineData("media", "media")]
    [InlineData("  media/movies  ", "media/movies")]
    [InlineData("./media", "./media")]
    [InlineData("~/media", "~/media")]
    [InlineData("C:media", "C:media")]
    [InlineData("C:", "C:")]
    [InlineData(@"\media", @"\media")]
    [InlineData(@"\\nas", @"\\nas")]
    public void Validate_with_a_path_that_is_not_absolute_on_linux_is_the_rule(string path, string echoed)
    {
        //Arrange
        string?[] paths = [path];

        //Act
        var error = AllowedRootRules.Validate(paths, PathStyle.Posix);

        //Assert
        Assert.Equal(string.Format(LinuxNotAFullPathError, echoed), error);
    }

    [Theory]
    [InlineData("media", "media")]
    [InlineData(" Media\\Movies ", "Media\\Movies")]
    [InlineData("~/media", "~/media")]
    [InlineData("C:media", "C:media")]
    [InlineData(@"\Media", @"\Media")]
    [InlineData(@"\\nas", @"\\nas")]
    public void Validate_with_a_path_that_is_not_absolute_on_windows_is_the_rule(string path, string echoed)
    {
        //Arrange
        string?[] paths = [path];

        //Act
        var error = AllowedRootRules.Validate(paths, PathStyle.Windows);

        //Assert
        Assert.Equal(string.Format(WindowsNotAFullPathError, echoed), error);
    }

    [Theory]
    [InlineData("/media/../etc", PathStyle.Posix, "/media/../etc")]
    [InlineData(" /media/./movies/ ", PathStyle.Posix, "/media/./movies/")]
    [InlineData("/..", PathStyle.Posix, "/..")]
    [InlineData(@"D:\Media\..", PathStyle.Windows, @"D:\Media\..")]
    [InlineData("d:/Media/./Movies", PathStyle.Windows, "d:/Media/./Movies")]
    [InlineData(@"\\nas\..\media", PathStyle.Windows, @"\\nas\..\media")]
    // Unreadable before it is of the wrong style: the sentence to act on first is the one about the dots.
    [InlineData(@"D:\Media\..", PathStyle.Posix, @"D:\Media\..")]
    public void Validate_with_a_dot_segment_is_the_rule(string path, PathStyle appStyle, string echoed)
    {
        //Arrange
        string?[] paths = [path];

        //Act
        var error = AllowedRootRules.Validate(paths, appStyle);

        //Assert
        // Echoed as typed (trimmed): it never parsed, so it has no canonical form.
        Assert.Equal(string.Format(DotSegmentError, echoed), error);
    }

    [Theory]
    [InlineData(@"D:\Media", @"D:\Media")]
    [InlineData(" d:/media/ ", @"D:\media")]
    [InlineData(@"\\nas\share\movies\", @"\\nas\share\movies")]
    // Of the wrong style before it is the whole file system: it could never hold a file the app opens.
    [InlineData(@"D:\", @"D:\")]
    [InlineData(@"\\nas\share", @"\\nas\share")]
    public void Validate_with_a_windows_path_when_the_app_runs_on_linux_is_the_rule(string path, string echoed)
    {
        //Arrange
        string?[] paths = ["/media", path];

        //Act
        var error = AllowedRootRules.Validate(paths, PathStyle.Posix);

        //Assert
        // Echoed in canonical form, the way it would have been stored.
        Assert.Equal(string.Format(WindowsPathOnLinuxError, echoed), error);
    }

    [Theory]
    [InlineData("/media", "/media")]
    [InlineData(" //media//movies/ ", "/media/movies")]
    [InlineData("/", "/")]
    public void Validate_with_a_linux_path_when_the_app_runs_on_windows_is_the_rule(string path, string echoed)
    {
        //Arrange
        string?[] paths = [@"D:\Media", path];

        //Act
        var error = AllowedRootRules.Validate(paths, PathStyle.Windows);

        //Assert
        Assert.Equal(string.Format(LinuxPathOnWindowsError, echoed), error);
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData(" /// ", "/")]
    public void Validate_with_the_whole_file_system_on_linux_is_the_rule(string path, string echoed)
    {
        //Arrange
        string?[] paths = [path];

        //Act
        var error = AllowedRootRules.Validate(paths, PathStyle.Posix);

        //Assert
        Assert.Equal(string.Format(LinuxWholeFileSystemError, echoed), error);
    }

    [Theory]
    [InlineData(@"D:\", @"D:\")]
    [InlineData("d:/", @"D:\")]
    // A share is the whole of what the server offers there: it names no folder either.
    [InlineData(@"\\nas\share", @"\\nas\share")]
    [InlineData(@"\\nas\share\\", @"\\nas\share")]
    public void Validate_with_the_whole_file_system_on_windows_is_the_rule(string path, string echoed)
    {
        //Arrange
        string?[] paths = [path];

        //Act
        var error = AllowedRootRules.Validate(paths, PathStyle.Windows);

        //Assert
        Assert.Equal(string.Format(WindowsWholeFileSystemError, echoed), error);
    }

    [Theory]
    [InlineData("/media", "/media/", PathStyle.Posix, "/media")]
    [InlineData(" /media/movies", "//media//movies", PathStyle.Posix, "/media/movies")]
    // Windows ignores letter case: one folder however it is typed. The first spelling is named.
    [InlineData(@"D:\Media", @"d:\media", PathStyle.Windows, @"D:\Media")]
    [InlineData("d:/media", @"D:\Media\", PathStyle.Windows, @"D:\media")]
    [InlineData(@"\\NAS\Share\x", @"\\nas\share\X", PathStyle.Windows, @"\\NAS\Share\x")]
    public void Validate_with_one_folder_twice_is_the_rule(string first, string second, PathStyle appStyle, string echoed)
    {
        //Arrange
        // After another folder, so the pair is found wherever it sits in the list.
        string?[] paths = [appStyle == PathStyle.Posix ? "/srv" : @"E:\Srv", first, second];

        //Act
        var error = AllowedRootRules.Validate(paths, appStyle);

        //Assert
        Assert.Equal(string.Format(ListedTwiceError, echoed), error);
    }

    [Fact]
    public void Validate_accepts_linux_folders_that_differ_only_in_letter_case()
    {
        //Arrange
        // ext4 is case-sensitive: these are two folders, and neither holds the other.
        string?[] paths = ["/Media", "/media", "/MEDIA/movies"];

        //Act
        var error = AllowedRootRules.Validate(paths, PathStyle.Posix);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("/media", "/media/movies", PathStyle.Posix, "/media/movies", "/media")]
    [InlineData("/media/movies/", "/media", PathStyle.Posix, "/media/movies", "/media")]
    [InlineData("/media", "/media/movies/4k", PathStyle.Posix, "/media/movies/4k", "/media")]
    // Inside in another letter case, in either order: Windows reads them as one folder and its child.
    [InlineData(@"D:\Media", @"d:\media\Movies", PathStyle.Windows, @"D:\media\Movies", @"D:\Media")]
    [InlineData(@"d:\media\Movies", @"D:\Media", PathStyle.Windows, @"D:\media\Movies", @"D:\Media")]
    [InlineData(@"\\nas\share\media", @"\\NAS\SHARE\Media\Movies", PathStyle.Windows, @"\\NAS\SHARE\Media\Movies", @"\\nas\share\media")]
    public void Validate_with_a_folder_inside_another_is_the_rule(
        string first, string second, PathStyle appStyle, string inner, string outer)
    {
        //Arrange
        string?[] paths = [first, second];

        //Act
        var error = AllowedRootRules.Validate(paths, appStyle);

        //Assert
        // The inner one is named first, then the outer one that already allows it, whichever came first.
        Assert.Equal(string.Format(InsideAnotherError, inner, outer), error);
    }

    [Theory]
    [InlineData(PathStyle.Posix, new[] { "/media", "/media2", "/media-old/movies", "/data/media" })]
    [InlineData(PathStyle.Windows, new[] { @"D:\Media", @"D:\Media2", @"E:\Media\Movies", @"\\nas\media\Movies" })]
    public void Validate_accepts_folders_whose_names_only_start_alike(PathStyle appStyle, string[] paths)
    {
        //Arrange

        //Act
        // Compared folder name by folder name, never as text: /media2 is not inside /media.
        var error = AllowedRootRules.Validate(paths, appStyle);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [MemberData(nameof(FirstFailures))]
    public void Validate_reports_the_first_rule_broken(string[] paths, string expected)
    {
        //Arrange

        //Act
        var error = AllowedRootRules.Validate(paths, PathStyle.Posix);

        //Assert
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Normalize_trims_writes_the_canonical_form_and_keeps_the_order()
    {
        //Arrange
        string?[] paths =
        [
            "  /srv//tv/  ",
            "/media",
            " d:/Media\\\\Movies\\ ",
            @"\\nas\share\music\",
            "\tC:\\Data\n",
        ];

        //Act
        var normalized = AllowedRootRules.Normalize(paths);

        //Assert
        // Not sorted: the page shows the list back in the order the owner typed it. The letter case of
        // a folder name is kept even on Windows; only the drive letter is upper-cased.
        Assert.Equal(["/srv/tv", "/media", @"D:\Media\Movies", @"\\nas\share\music", @"C:\Data"], normalized);
    }

    [Fact]
    public void Normalize_of_a_path_that_does_not_parse_throws()
    {
        //Arrange
        string?[] paths = ["/media", "media"];

        //Act
        var exception = Record.Exception(() => AllowedRootRules.Normalize(paths));

        //Assert
        // A caller that skipped Validate fails loudly instead of storing a guess.
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void PlatformStyle_is_windows_exactly_when_the_app_runs_on_windows()
    {
        //Arrange
        var expected = OperatingSystem.IsWindows() ? PathStyle.Windows : PathStyle.Posix;

        //Act
        var style = AllowedRootRules.PlatformStyle;

        //Assert
        Assert.Equal(expected, style);
    }
}
