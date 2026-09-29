using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Tests.Paths;

public sealed class NormalizedPathTests
{
    [Theory]
    [InlineData("/downloads/movies", new[] { "downloads", "movies" })]
    [InlineData("/downloads//movies/", new[] { "downloads", "movies" })]
    [InlineData("//downloads///movies//", new[] { "downloads", "movies" })]
    [InlineData("/", new string[0])]
    [InlineData("///", new string[0])]
    public void TryParse_reads_a_posix_path_and_collapses_repeated_and_trailing_slashes(string text, string[] segments)
    {
        //Arrange

        //Act
        var parsed = NormalizedPath.TryParse(text, out var path, out var error);

        //Assert
        Assert.True(parsed);
        Assert.Equal(PathParseError.None, error);
        Assert.Equal(PathStyle.Posix, path!.Style);
        Assert.Equal("/", path.Root);
        Assert.Equal(segments, path.Segments);
    }

    [Theory]
    [InlineData(@"D:\Torrents\Movies", "D:", new[] { "Torrents", "Movies" })]
    [InlineData("d:/Torrents/Movies/", "D:", new[] { "Torrents", "Movies" })]
    [InlineData(@"d:\Torrents/Movies\\", "D:", new[] { "Torrents", "Movies" })]
    [InlineData(@"c:\", "C:", new string[0])]
    public void TryParse_reads_a_windows_drive_path_with_either_separator_and_upper_cases_the_drive(string text, string root, string[] segments)
    {
        //Arrange

        //Act
        var parsed = NormalizedPath.TryParse(text, out var path, out var error);

        //Assert
        Assert.True(parsed);
        Assert.Equal(PathParseError.None, error);
        Assert.Equal(PathStyle.Windows, path!.Style);
        Assert.Equal(root, path.Root);
        Assert.Equal(segments, path.Segments);
    }

    [Theory]
    [InlineData(@"\\nas\media\movies", @"\\nas\media", new[] { "movies" })]
    [InlineData(@"\\nas\media\", @"\\nas\media", new string[0])]
    [InlineData(@"\\nas/media/movies/", @"\\nas\media", new[] { "movies" })]
    public void TryParse_reads_a_unc_path(string text, string root, string[] segments)
    {
        //Arrange

        //Act
        var parsed = NormalizedPath.TryParse(text, out var path, out var error);

        //Assert
        Assert.True(parsed);
        Assert.Equal(PathParseError.None, error);
        Assert.Equal(PathStyle.Windows, path!.Style);
        Assert.Equal(root, path.Root);
        Assert.Equal(segments, path.Segments);
    }

    [Theory]
    [InlineData("movies", PathParseError.NotAbsolute)]
    [InlineData("~/x", PathParseError.NotAbsolute)]
    [InlineData("C:x", PathParseError.NotAbsolute)]
    [InlineData("C:", PathParseError.NotAbsolute)]
    [InlineData(@"\movies", PathParseError.NotAbsolute)]
    [InlineData(@"\\nas", PathParseError.NotAbsolute)]
    [InlineData(" /media", PathParseError.NotAbsolute)]
    [InlineData("", PathParseError.Blank)]
    [InlineData("   ", PathParseError.Blank)]
    [InlineData(null, PathParseError.Blank)]
    public void TryParse_refuses_a_relative_path(string? text, PathParseError expected)
    {
        //Arrange

        //Act
        var parsed = NormalizedPath.TryParse(text, out var path, out var error);

        //Assert
        Assert.False(parsed);
        Assert.Null(path);
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("/media/./movies")]
    [InlineData("/media/../etc")]
    [InlineData("/..")]
    [InlineData(@"D:\Torrents\..\Windows")]
    [InlineData(@"\\nas\..\movies")]
    [InlineData(@"\\.\pipe\x")]
    public void TryParse_refuses_dot_and_dot_dot_segments(string text)
    {
        //Arrange

        //Act
        var parsed = NormalizedPath.TryParse(text, out var path, out var error);

        //Assert
        Assert.False(parsed);
        Assert.Null(path);
        Assert.Equal(PathParseError.DotSegment, error);
    }

    [Theory]
    [InlineData("/media/mov\nies")]
    [InlineData("/media/movies\r")]
    [InlineData("/media/\tmovies")]
    [InlineData("D:\\Torrents\u0000")]
    public void TryParse_refuses_control_characters(string text)
    {
        //Arrange

        //Act
        var parsed = NormalizedPath.TryParse(text, out var path, out var error);

        //Assert
        Assert.False(parsed);
        Assert.Null(path);
        Assert.Equal(PathParseError.ControlCharacter, error);
    }

    [Theory]
    [InlineData("/downloads", "/downloads", true)]
    [InlineData("/downloads/movies", "/downloads", true)]
    [InlineData("/downloads/movies/x.mkv", "/downloads", true)]
    [InlineData("/downloads/movies", "/", true)]
    [InlineData("/downloads2/movies", "/downloads", false)]
    [InlineData("/down", "/downloads", false)]
    [InlineData("/downloads", "/downloads/movies", false)]
    [InlineData(@"D:\downloads\movies", "/downloads", false)]
    public void IsUnder_is_segment_wise(string pathText, string prefixText, bool expected)
    {
        //Arrange
        var path = Parse(pathText);
        var prefix = Parse(prefixText);

        //Act
        var isUnder = path.IsUnder(prefix);

        //Assert
        Assert.Equal(expected, isUnder);
    }

    [Fact]
    public void IsUnder_is_case_sensitive_for_posix()
    {
        //Arrange
        var path = Parse("/Downloads/movies");
        var prefix = Parse("/downloads");

        //Act
        var isUnder = path.IsUnder(prefix);
        var isSame = Parse("/Downloads").IsSameAs(prefix);

        //Assert
        Assert.False(isUnder);
        Assert.False(isSame);
    }

    [Fact]
    public void IsUnder_ignores_case_for_windows()
    {
        //Arrange
        var path = Parse(@"d:\TORRENTS\Movies\x.mkv");
        var prefix = Parse(@"D:\torrents");
        var uncPath = Parse(@"\\NAS\Media\movies");
        var uncPrefix = Parse(@"\\nas\media");

        //Act
        var isUnder = path.IsUnder(prefix);
        var uncIsUnder = uncPath.IsUnder(uncPrefix);
        var isSame = Parse(@"D:\Torrents").IsSameAs(prefix);

        //Assert
        Assert.True(isUnder);
        Assert.True(uncIsUnder);
        Assert.True(isSame);
    }

    [Fact]
    public void SegmentsAfter_is_what_lies_below_the_prefix()
    {
        //Arrange
        var path = Parse("/downloads/movies/Taken (2008)/Taken.mkv");
        var prefix = Parse("/downloads");

        //Act
        var remainder = path.SegmentsAfter(prefix);
        var ofItself = prefix.SegmentsAfter(prefix);

        //Assert
        Assert.Equal(new[] { "movies", "Taken (2008)", "Taken.mkv" }, remainder);
        Assert.Empty(ofItself);
    }

    [Fact]
    public void Equality_is_the_styles_comparison_not_the_segment_lists_reference()
    {
        //Arrange
        var first = Parse(@"D:\Torrents\Movies");
        var second = Parse("d:/torrents/movies/");
        var posix = Parse("/media/movies");

        //Act
        var windowsEqual = first == second;
        var hashesEqual = first.GetHashCode() == second.GetHashCode();
        var posixEqual = posix == Parse("/media//movies/");
        var posixCaseEqual = posix == Parse("/media/Movies");

        //Assert
        Assert.True(windowsEqual);
        Assert.True(hashesEqual);
        Assert.True(posixEqual);
        Assert.False(posixCaseEqual);
    }

    [Theory]
    [InlineData("/downloads//movies/", "/downloads/movies")]
    [InlineData("/", "/")]
    [InlineData("//", "/")]
    [InlineData("d:/DL/", @"D:\DL")]
    [InlineData(@"d:\", @"D:\")]
    [InlineData(@"\\nas\media\", @"\\nas\media")]
    [InlineData(@"\\nas/media//movies/", @"\\nas\media\movies")]
    [InlineData("/media/Чужі (1986)", "/media/Чужі (1986)")]
    public void ToString_is_the_canonical_form(string text, string expected)
    {
        //Arrange
        var path = Parse(text);

        //Act
        var canonical = path.ToString();

        //Assert
        Assert.Equal(expected, canonical);
    }

    [Fact]
    public void Append_puts_the_segments_below_the_path_in_its_own_style()
    {
        //Arrange
        var prefix = Parse(@"D:\Media");

        //Act
        var appended = prefix.Append(["movies", "x.mkv"]);

        //Assert
        Assert.Equal(@"D:\Media\movies\x.mkv", appended.ToString());
        Assert.Equal(@"D:\Media", prefix.ToString());
    }

    private static NormalizedPath Parse(string text) =>
        NormalizedPath.TryParse(text, out var path, out var error)
            ? path
            : throw new ArgumentException($"The test path “{text}” didn't parse: {error}.", nameof(text));
}
