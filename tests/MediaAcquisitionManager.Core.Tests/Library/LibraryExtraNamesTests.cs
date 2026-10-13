using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-43 — Jellyfin's names for a film's extras (Emby.Naming NamingOptions.VideoExtraRules), which say why a video in a
// library's folders is in no library: the file's own folder by its name, letter case ignored, never at the library's
// root and never a folder further up; the whole file name trailer or sample, trailing digits trimmed; or a suffix of it.
public sealed class LibraryExtraNamesTests
{
    private const string Movies = "/media/movies";

    [Theory]
    [InlineData("/media/cartoons/Toy Story. Collection (1995-2022)/Shorts/[01] Hawaiian Vacation (2011).mkv", "Shorts")]
    [InlineData("/media/movies/Alien. Collection/Shorts/[01] Alien. Containment (2019).mkv", "Shorts")]
    [InlineData("/media/movies/Dune (2021)/trailers/Dune.mkv", "trailers")]
    [InlineData("/media/movies/Dune (2021)/Behind The Scenes/Making of.mkv", "Behind The Scenes")]
    [InlineData("/media/movies/Dune (2021)/Deleted Scenes/1.mkv", "Deleted Scenes")]
    [InlineData("/media/movies/Dune (2021)/FEATURETTES/a.mkv", "FEATURETTES")]
    [InlineData("/media/movies/Dune (2021)/Extras/a.mkv", "Extras")]
    [InlineData("/media/movies/Dune (2021)/extra/a.mkv", "extra")]
    [InlineData("/media/movies/Dune (2021)/Other/a.mkv", "Other")]
    [InlineData("/media/movies/Dune (2021)/Clips/a.mkv", "Clips")]
    [InlineData("/media/movies/Dune (2021)/Interviews/a.mkv", "Interviews")]
    [InlineData("/media/movies/Dune (2021)/Scenes/a.mkv", "Scenes")]
    [InlineData("/media/movies/Dune (2021)/Samples/a.mkv", "Samples")]
    [InlineData("/media/movies/Dune (2021)/sample/a.mkv", "sample")]
    [InlineData("/media/movies/Dune (2021)/backdrops/a.mkv", "backdrops")]
    public void ExtrasFolderOf_a_video_in_an_extras_folder_is_the_folders_name_as_on_the_disk(string path, string folder)
    {
        //Arrange
        var file = PathOf(path);

        //Act
        var name = LibraryExtraNames.ExtrasFolderOf(file, PathOf(LibraryRootOf(path)));

        //Assert
        Assert.Equal(folder, name);
    }

    [Theory]
    // Right under the library's root, Jellyfin applies no folder rule, whatever the root is called.
    [InlineData("/media/Shorts/a.mkv", "/media/Shorts")]
    // Only the file's own folder counts, not one further up.
    [InlineData("/media/movies/Shorts/Alien (1979)/Alien (1979).mkv", Movies)]
    [InlineData("/media/movies/Dune (2021)/Dune (2021).mkv", Movies)]
    [InlineData("/media/movies/Dune (2021)/Short/a.mkv", Movies)]
    [InlineData("/media/movies/Dune (2021)/Shorts 2/a.mkv", Movies)]
    // Theme music is an audio rule.
    [InlineData("/media/movies/Dune (2021)/theme-music/a.mkv", Movies)]
    public void ExtrasFolderOf_a_video_in_no_extras_folder_is_null(string path, string libraryFolder)
    {
        //Arrange
        var file = PathOf(path);

        //Act
        var name = LibraryExtraNames.ExtrasFolderOf(file, PathOf(libraryFolder));

        //Assert
        Assert.Null(name);
    }

    [Theory]
    [InlineData("trailer.mkv", "trailer")]
    [InlineData("Trailer2.mp4", "trailer")]
    [InlineData("sample.mkv", "sample")]
    [InlineData("SAMPLE01.mkv", "sample")]
    [InlineData("Dune (2021)-trailer.mkv", "-trailer")]
    [InlineData("Dune (2021).trailer.mkv", ".trailer")]
    [InlineData("Dune (2021)_trailer.mkv", "_trailer")]
    [InlineData("Dune (2021) - trailer.mkv", "- trailer")]
    [InlineData("dirty.dozen-sample.mkv", "-sample")]
    [InlineData("dirty.dozen.sample.mkv", ".sample")]
    [InlineData("dirty.dozen_sample1.mkv", "_sample")]
    [InlineData("Dune - Sample.mkv", "- sample")]
    [InlineData("Dune-scene.mkv", "-scene")]
    [InlineData("Dune-clip.mkv", "-clip")]
    [InlineData("Dune-interview.mkv", "-interview")]
    [InlineData("Dune-BehindTheScenes.mkv", "-behindthescenes")]
    [InlineData("Dune-deleted.mkv", "-deleted")]
    [InlineData("Dune-deletedscene.mkv", "-deletedscene")]
    [InlineData("Dune-featurette.mkv", "-featurette")]
    [InlineData("Dune-short.mkv", "-short")]
    [InlineData("Dune-extra.mkv", "-extra")]
    [InlineData("Dune-other.mkv", "-other")]
    public void ExtraNameOf_a_name_an_extra_rule_matches_is_the_rule(string fileName, string rule)
    {
        //Arrange

        //Act
        var name = LibraryExtraNames.ExtraNameOf(fileName);

        //Assert
        Assert.Equal(rule, name);
    }

    [Theory]
    [InlineData("Dune (2021).mkv")]
    // A space is no rule's: Jellyfin lists this one, so it isn't why a library skipped it.
    [InlineData("Dune (2021) sample.mkv")]
    [InlineData("trailers.mkv")]
    [InlineData("Sampler.mkv")]
    [InlineData("[01] Hawaiian Vacation (2011) BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv")]
    [InlineData("00000.m2ts")]
    [InlineData("theme.mkv")]
    public void ExtraNameOf_a_name_no_rule_matches_is_null(string fileName)
    {
        //Arrange

        //Act
        var name = LibraryExtraNames.ExtraNameOf(fileName);

        //Assert
        Assert.Null(name);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string LibraryRootOf(string path) => path.StartsWith("/media/cartoons/", StringComparison.Ordinal) ? "/media/cartoons" : Movies;

    private static NormalizedPath PathOf(string text)
    {
        Assert.True(NormalizedPath.TryParse(text, out var path, out _), $"“{text}” doesn't parse.");
        return path;
    }
}
