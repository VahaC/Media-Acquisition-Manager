using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-11 — the folder a qBittorrent category saves to, worked out as qBittorrent's own categorySavePath
// does it (session.cpp of 4.4.5, sessionimpl.cpp of 4.6.7, 5.1.4 and 5.2.3): torrents/categories lists the
// stored option, which may be empty or relative, never the folder. Anything that can't be worked out
// exactly is null, which keeps automatic replacement off for the destination.
public sealed class QBittorrentCategoryPathsTests
{
    private const string DefaultSavePath = "/downloads";

    /// <summary>The versions on either side of each change of the rules: 4.4.1 (the oldest taken), 4.6.7
    /// (the last whole-name one), 5.0.0 and 5.1.4 (use_subcategories), 5.2.0 and 5.2.3 (always).</summary>
    public static TheoryData<string> Versions => ["4.4.1", "4.6.7", "5.0.0", "5.1.4", "5.2.0", "5.2.3"];

    /// <summary>The versions that list every save path cleaned and with "/" separators: 4.5.0 (the first)
    /// and one of each later line.</summary>
    public static TheoryData<string> CleanedPathVersions => ["4.5.0", "4.6.7", "5.0.0", "5.1.4", "5.2.3"];

    /// <summary>Names qBittorrent's toValidPath (and 4.4's toValidFileSystemName) would rewrite, or its path
    /// cleaning fold, before using them as folder names.</summary>
    public static TheoryData<string> RewrittenNames =>
    [
        "Movies: HD",
        "What?",
        "Star*",
        "Quote\"d",
        "Less<",
        "More>",
        "Pipe|",
        " Movies",
        "Movies ",
        "Back\\slash",
        ".",
        "..",
    ];

    [Theory]
    [MemberData(nameof(Versions))]
    public void FolderOf_an_absolute_save_path_is_that_folder_in_every_version(string version)
    {
        //Arrange
        var savePaths = SavePaths(("Movies", "/downloads/movies/"));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Movies", savePaths, null);
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(Version.Parse(version), ["Movies"], savePaths);

        //Assert
        Assert.Equal("/downloads/movies", folder);
        Assert.False(needsDefault);
    }

    [Theory]
    [InlineData("5.2.3", "D:/Torrents/Movies", @"D:\Torrents\Movies")]
    [InlineData("4.4.5", "D:/Torrents/Movies", @"D:\Torrents\Movies")]
    [InlineData("4.4.5", @"d:\Torrents\Movies\", @"D:\Torrents\Movies")]
    [InlineData("4.4.5", @"\\nas\media\Movies", @"\\nas\media\Movies")]
    public void FolderOf_a_windows_hosts_absolute_save_path_is_that_folder_in_its_canonical_form(string version, string savePath, string expected)
    {
        //Arrange
        // 5.2 lists a Windows host's folder with forward slashes (Path::data()); 4.4 as it was typed.
        var savePaths = SavePaths(("Movies", savePath));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Movies", savePaths, null);

        //Assert
        Assert.Equal(expected, folder);
    }

    [Theory]
    [InlineData("4.5.0", @"\\nas\media\Movies", @"/downloads/\\nas\media\Movies")]
    [InlineData("5.2.3", @"\\nas\media\Movies", @"/downloads/\\nas\media\Movies")]
    [InlineData("5.2.3", @"D:\Torrents\Movies", @"/downloads/D:\Torrents\Movies")]
    public void FolderOf_a_save_path_with_a_backslash_from_4_5_is_a_relative_folder_of_a_posix_host(string version, string savePath, string expected)
    {
        //Arrange
        // From 4.5 a Windows host lists "/" only, so a backslash comes from another host, where this is a
        // relative path whose folder name holds the backslashes — never a Windows folder.
        var savePaths = SavePaths(("Movies", savePath));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(Version.Parse(version), ["Movies"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Movies", savePaths, DefaultSavePath);

        //Assert
        Assert.True(needsDefault);
        Assert.Equal(expected, folder);
    }

    [Theory]
    [InlineData(@"\\nas\media\Movies")]
    [InlineData(@"films\hd")]
    public void FolderOf_a_save_path_with_a_backslash_from_4_5_under_a_windows_default_is_unknown(string savePath)
    {
        //Arrange
        // A Windows host lists neither a backslash in a save path (Path::data()) nor a default save path
        // without one (Path::toString()): the two answers contradict each other.
        var savePaths = SavePaths(("Movies", savePath));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies", savePaths, @"D:\Torrents");

        //Assert
        Assert.Null(folder);
    }

    [Theory]
    [MemberData(nameof(CleanedPathVersions))]
    public void FolderOf_a_save_path_with_a_leading_double_slash_from_4_5_is_a_windows_hosts_unc_folder(string version)
    {
        //Arrange
        // \\nas\media\Movies as Path::data() lists it: Qt's QDir::cleanPath keeps the leading "//" only on
        // Windows, and folds it into one "/" on any other host.
        var savePaths = SavePaths(("Movies", "//nas/media/Movies"));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(Version.Parse(version), ["Movies"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Movies", savePaths, DefaultSavePath);

        //Assert
        Assert.False(needsDefault);
        Assert.Equal(@"\\nas\media\Movies", folder);
    }

    [Theory]
    [InlineData("4.4.1")]
    [InlineData("4.4.5")]
    public void FolderOf_a_save_path_with_a_leading_double_slash_on_4_4_is_unknown(string version)
    {
        //Arrange
        // 4.4 lists the save path as it was typed: a Windows host's UNC folder, or a POSIX one that Linux
        // reads as /nas/media/Movies.
        var savePaths = SavePaths(("Movies", "//nas/media/Movies"));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(Version.Parse(version), ["Movies"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Movies", savePaths, DefaultSavePath);

        //Assert
        Assert.False(needsDefault);
        Assert.Null(folder);
    }

    [Theory]
    [InlineData("//")]
    [InlineData("//nas")]
    [InlineData("//nas/")]
    [InlineData("///nas/media/Movies")]
    [InlineData("//nas/../Movies")]
    public void FolderOf_a_save_path_with_a_leading_double_slash_that_names_no_unc_folder_is_unknown(string savePath)
    {
        //Arrange
        // A server without a share, or a third leading slash: no folder, and never the POSIX one the text
        // would otherwise parse as.
        var savePaths = SavePaths(("Movies", savePath));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(new Version(5, 2, 3), ["Movies"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies", savePaths, DefaultSavePath);

        //Assert
        Assert.False(needsDefault);
        Assert.Null(folder);
    }

    [Fact]
    public void FolderOf_a_subcategory_on_5_2_under_a_unc_parent_is_inside_that_unc_folder()
    {
        //Arrange
        var savePaths = SavePaths(("Movies", "//nas/media/Movies"), ("Movies/4K", ""));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(new Version(5, 2, 3), ["Movies", "Movies/4K"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies/4K", savePaths, null);

        //Assert
        Assert.False(needsDefault);
        Assert.Equal(@"\\nas\media\Movies\4K", folder);
    }

    [Theory]
    [InlineData("4.4.1")]
    [InlineData("5.2.3")]
    public void FolderOf_a_category_under_a_default_save_path_with_a_leading_double_slash_is_unknown(string version)
    {
        //Arrange
        // 4.4 answers the default save path with a Windows host's backslashes turned into "/", so a UNC
        // folder and a POSIX one look alike; 4.5 and later never answer one (a Windows host's comes with
        // backslashes, Path::toString(); any other is cleaned).
        var savePaths = SavePaths(("Movies", ""), ("Series", "series"));

        //Act
        var implicitFolder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Movies", savePaths, "//nas/torrents");
        var relativeFolder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Series", savePaths, "//nas/torrents");

        //Assert
        Assert.Null(implicitFolder);
        Assert.Null(relativeFolder);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void FolderOf_a_top_level_category_without_a_save_path_is_its_name_under_the_default_save_path(string version)
    {
        //Arrange
        var savePaths = SavePaths(("Фільми 4K", ""));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(Version.Parse(version), ["Фільми 4K"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Фільми 4K", savePaths, DefaultSavePath);

        //Assert
        Assert.True(needsDefault);
        Assert.Equal("/downloads/Фільми 4K", folder);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void FolderOf_a_relative_save_path_is_under_the_default_save_path_a_subcategorys_too(string version)
    {
        //Arrange
        // The parent's own folder plays no part: qBittorrent joins an explicit relative path to the default.
        var savePaths = SavePaths(("Movies", "/media/films"), ("Movies/4K", "uhd/remux/"), ("Series", "series"));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(Version.Parse(version), ["Movies/4K"], savePaths);
        var subcategory = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Movies/4K", savePaths, DefaultSavePath);
        var topLevel = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Series", savePaths, DefaultSavePath);

        //Assert
        Assert.True(needsDefault);
        Assert.Equal("/downloads/uhd/remux", subcategory);
        Assert.Equal("/downloads/series", topLevel);
    }

    [Theory]
    [InlineData("4.4.1", "/downloads/Movies/4K")]
    [InlineData("4.6.7", "/downloads/Movies/4K")]
    [InlineData("5.0.0", null)]
    [InlineData("5.1.4", null)]
    [InlineData("5.2.0", "/media/films/4K")]
    [InlineData("5.2.3", "/media/films/4K")]
    public void FolderOf_a_subcategory_without_a_save_path_follows_the_rules_of_the_version_that_lists_it(string version, string? expected)
    {
        //Arrange
        // Up to 4.6 the whole name under the default; 5.2 the parent's folder; 5.0 and 5.1 either, as
        // use_subcategories says — and only app/preferences, which the app never reads, says it.
        var savePaths = SavePaths(("Movies", "/media/films"), ("Movies/4K", ""));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), "Movies/4K", savePaths, DefaultSavePath);

        //Assert
        Assert.Equal(expected, folder);
    }

    [Theory]
    [InlineData("4.6.7", true)]
    [InlineData("5.1.4", false)]
    [InlineData("5.2.3", false)]
    public void NeedsDefaultSavePath_of_a_subcategory_under_an_absolute_parent_asks_only_where_the_default_is_used(string version, bool expected)
    {
        //Arrange
        var savePaths = SavePaths(("Movies", "/media/films"), ("Movies/4K", ""));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(Version.Parse(version), ["Movies", "Movies/4K"], savePaths);

        //Assert
        Assert.Equal(expected, needsDefault);
    }

    [Fact]
    public void FolderOf_a_subcategory_on_5_2_whose_parent_is_not_listed_takes_the_parent_as_one_without_a_save_path()
    {
        //Arrange
        // 5.2's categoryOptions() of a name it doesn't hold is the default options: an empty save path.
        var savePaths = SavePaths(("Movies/4K", ""));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(new Version(5, 2, 3), ["Movies/4K"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies/4K", savePaths, DefaultSavePath);

        //Assert
        Assert.True(needsDefault);
        Assert.Equal("/downloads/Movies/4K", folder);
    }

    [Fact]
    public void FolderOf_a_subcategory_on_5_2_follows_every_parent_up_to_one_with_a_folder()
    {
        //Arrange
        var savePaths = SavePaths(("Video", "/media"), ("Video/Movies", ""), ("Video/Movies/4K", ""));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(new Version(5, 2, 3), ["Video/Movies/4K"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Video/Movies/4K", savePaths, null);

        //Assert
        Assert.False(needsDefault);
        Assert.Equal("/media/Movies/4K", folder);
    }

    [Fact]
    public void FolderOf_a_subcategory_on_5_2_whose_parent_gives_no_save_path_as_text_is_unknown()
    {
        //Arrange
        var savePaths = SavePaths(("Movies", null), ("Movies/4K", ""));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies/4K", savePaths, DefaultSavePath);

        //Assert
        Assert.Null(folder);
    }

    [Theory]
    [MemberData(nameof(RewrittenNames))]
    public void FolderOf_a_category_qbittorrent_would_rename_on_disk_is_unknown_without_asking_for_the_default(string name)
    {
        //Arrange
        var savePaths = SavePaths((name, ""));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(new Version(5, 2, 3), [name], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), name, savePaths, DefaultSavePath);

        //Assert
        Assert.False(needsDefault);
        Assert.Null(folder);
    }

    [Theory]
    [InlineData("4.6.7", "Movies/ 4K")]
    [InlineData("4.6.7", "Movies/4K: HDR")]
    [InlineData("5.2.3", "Movies/4K: HDR")]
    [InlineData("5.2.3", "Movies/..")]
    public void FolderOf_a_subcategory_whose_name_qbittorrent_would_rewrite_is_unknown(string version, string name)
    {
        //Arrange
        var savePaths = SavePaths(("Movies", "/media/films"), (name, ""));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(Version.Parse(version), name, savePaths, DefaultSavePath);

        //Assert
        Assert.Null(folder);
    }

    [Fact]
    public void FolderOf_a_category_with_a_rewritten_name_but_its_own_absolute_save_path_is_that_folder()
    {
        //Arrange
        // The name only becomes a folder when the save path is empty.
        var savePaths = SavePaths(("Movies: HD", "/downloads/hd"));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies: HD", savePaths, null);

        //Assert
        Assert.Equal("/downloads/hd", folder);
    }

    [Theory]
    [InlineData(@"D:\Torrents", @"D:\Torrents\Movies")]
    [InlineData("D:/Torrents/", @"D:\Torrents\Movies")]
    [InlineData(@"\\nas\torrents", @"\\nas\torrents\Movies")]
    [InlineData("/downloads/", "/downloads/Movies")]
    public void FolderOf_reads_the_default_save_path_as_each_version_and_host_reports_it(string defaultSavePath, string expected)
    {
        //Arrange
        // app/defaultSavePath answers a Windows host's folder with backslashes from 4.5 on
        // (Path::toString()), and 4.4 with forward slashes and maybe a trailing one.
        var savePaths = SavePaths(("Movies", ""));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies", savePaths, defaultSavePath);

        //Assert
        Assert.Equal(expected, folder);
    }

    [Theory]
    [InlineData(@"films\hd", "/downloads/films\\hd")]
    [InlineData("films/./hd", null)]
    [InlineData("../films", null)]
    public void FolderOf_a_relative_save_path_under_a_posix_default_splits_at_slashes_only_and_refuses_dot_folders(string savePath, string? expected)
    {
        //Arrange
        // On Linux a backslash is part of a folder name; "." and ".." qBittorrent would resolve.
        var savePaths = SavePaths(("Movies", savePath));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies", savePaths, DefaultSavePath);

        //Assert
        Assert.Equal(expected, folder);
    }

    [Fact]
    public void FolderOf_a_relative_save_path_under_a_windows_default_splits_at_either_slash()
    {
        //Arrange
        // 4.4 lists a Windows host's save path as it was saved, backslashes included.
        var savePaths = SavePaths(("Movies", @"films\hd/remux"));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(4, 4, 5), "Movies", savePaths, @"D:\Torrents");

        //Assert
        Assert.Equal(@"D:\Torrents\films\hd\remux", folder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("downloads")]
    [InlineData("~/Downloads")]
    [InlineData("/downloads/../incoming")]
    [InlineData("/downloads\n")]
    public void FolderOf_a_category_under_a_default_save_path_that_is_not_a_full_path_is_unknown(string? defaultSavePath)
    {
        //Arrange
        var savePaths = SavePaths(("Movies", ""), ("Series", "series"));

        //Act
        var implicitFolder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies", savePaths, defaultSavePath);
        var relativeFolder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Series", savePaths, defaultSavePath);

        //Assert
        Assert.Null(implicitFolder);
        Assert.Null(relativeFolder);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("/downloads/../movies")]
    [InlineData("/downloads/\u0007movies")]
    public void FolderOf_a_save_path_that_is_not_a_folder_as_written_is_unknown(string savePath)
    {
        //Arrange
        var savePaths = SavePaths(("Movies", savePath));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies", savePaths, DefaultSavePath);

        //Assert
        Assert.Null(folder);
    }

    [Fact]
    public void FolderOf_a_category_whose_entry_gives_no_save_path_as_text_is_unknown()
    {
        //Arrange
        var savePaths = SavePaths(("Movies", null));

        //Act
        var needsDefault = QBittorrentCategoryPaths.NeedsDefaultSavePath(new Version(5, 2, 3), ["Movies"], savePaths);
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies", savePaths, DefaultSavePath);

        //Assert
        Assert.False(needsDefault);
        Assert.Null(folder);
    }

    [Fact]
    public void FolderOf_a_folder_longer_than_a_path_column_holds_is_unknown()
    {
        //Arrange
        // 4096 characters at most (Linux's PATH_MAX); the default alone is 4090 here.
        var longDefault = "/" + new string('d', 4089);
        var savePaths = SavePaths(("Movies", ""), ("Docs", ""));

        //Act
        var tooLong = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Movies", savePaths, longDefault);
        var fits = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "Docs", savePaths, longDefault);

        //Assert
        Assert.Null(tooLong);
        Assert.Equal(4095, fits!.Length);
    }

    [Fact]
    public void FolderOf_under_a_default_save_path_longer_than_a_path_column_holds_is_unknown()
    {
        //Arrange
        var savePaths = SavePaths(("M", ""));

        //Act
        var folder = QBittorrentCategoryPaths.FolderOf(new Version(5, 2, 3), "M", savePaths, "/" + new string('d', 4096));

        //Assert
        Assert.Null(folder);
    }

    private static Dictionary<string, string?> SavePaths(params (string Name, string? SavePath)[] categories) =>
        categories.ToDictionary(category => category.Name, category => category.SavePath, StringComparer.Ordinal);
}
