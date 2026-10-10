using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-25, ADR 0035, ADR 0003 — which library holds a replacement's new file, and the file's path in that library's own
// terms: the folder's path as the library reported it, followed by the file's folders under the folder's app path — never
// the app's own path, and no mapping run backwards. A library listing a folder inside another of its own names the file
// under the deepest; each library holding it is a place of its own — the check stops on two (TwoLibrariesReason); a file
// outside every folder, or inside one whose integration or library path isn't known, has none. A pure rule of the
// verifier's with no endpoint of its own, tested as a parser is; the HTTP tests of the check run it end to end.
public sealed class ReplacementLibraryPlacesTests
{
    private static readonly Guid LivingRoom = Guid.Parse("0f8f2b52-7c4d-4a0e-9a35-5b8c1c0d2a11");
    private static readonly Guid Bedroom = Guid.Parse("6a1d9e30-2f47-4b8a-8e6c-93d4f7a0b522");

    [Fact]
    public void Of_a_file_in_a_folder_the_library_names_otherwise_is_its_path_in_the_librarys_terms()
    {
        //Arrange
        var appPath = Parsed("/media/movies/Alien (1979)/Alien.1979.Directors.Cut.2160p.mkv");
        LibraryFolder[] folders = [new("Фільми", "/media/movies", LivingRoom, "/data/movies")];

        //Act
        var places = ReplacementLibraryPlaces.Of(appPath, folders);

        //Assert
        Assert.Equal([new ReplacementLibraryPlace(LivingRoom, "Фільми", "/data/movies/Alien (1979)/Alien.1979.Directors.Cut.2160p.mkv")], places);
    }

    [Fact]
    public void Of_a_file_under_folders_of_one_library_one_inside_the_other_is_its_path_under_the_deepest()
    {
        //Arrange
        // Jellyfin's 4K folder is a mount of its own inside the two others the library lists — between them, so neither the
        // first nor the last folder holding the file is the one: the file is read under the deepest.
        var appPath = Parsed("/media/movies/4k/Alien.1979.Directors.Cut.2160p.mkv");
        LibraryFolder[] folders =
        [
            new("Фільми", "/media/movies", LivingRoom, "/data/movies"),
            new("Фільми", "/media/movies/4k", LivingRoom, "/mnt/uhd"),
            new("Фільми", "/media", LivingRoom, "/data"),
        ];

        //Act
        var places = ReplacementLibraryPlaces.Of(appPath, folders);

        //Assert
        Assert.Equal([new ReplacementLibraryPlace(LivingRoom, "Фільми", "/mnt/uhd/Alien.1979.Directors.Cut.2160p.mkv")], places);
    }

    [Fact]
    public void Of_a_file_in_folders_of_several_libraries_is_a_place_in_each_in_the_folders_order()
    {
        //Arrange
        // Another library of the same Jellyfin over the same folder, and a second Jellyfin whose library of the same name
        // reaches it by its own path: three libraries, each of which would read the file.
        var appPath = Parsed("/media/movies/Alien.1979.Directors.Cut.2160p.mkv");
        LibraryFolder[] folders =
        [
            new("Фільми", "/media/movies", LivingRoom, "/data/movies"),
            new("Фільми 4K", "/media/movies", LivingRoom, "/data/movies"),
            new("Фільми", "/media/movies", Bedroom, "/srv/films"),
        ];

        //Act
        var places = ReplacementLibraryPlaces.Of(appPath, folders);

        //Assert
        Assert.Equal(
            [
                new ReplacementLibraryPlace(LivingRoom, "Фільми", "/data/movies/Alien.1979.Directors.Cut.2160p.mkv"),
                new ReplacementLibraryPlace(LivingRoom, "Фільми 4K", "/data/movies/Alien.1979.Directors.Cut.2160p.mkv"),
                new ReplacementLibraryPlace(Bedroom, "Фільми", "/srv/films/Alien.1979.Directors.Cut.2160p.mkv"),
            ],
            places);
    }

    [Fact]
    public void Of_a_file_no_known_library_folder_holds_is_no_place()
    {
        //Arrange
        // A sibling whose name only begins like a library folder's — compared folder by folder, never as text —, and a
        // folder holding it whose library the list doesn't name, as a destination's check builds the list.
        var appPath = Parsed("/media/movies-old/Alien.1979.Directors.Cut.2160p.mkv");
        LibraryFolder[] folders =
        [
            new("Фільми", "/media/movies", LivingRoom, "/data/movies"),
            new("Старі фільми", "/media/movies-old"),
        ];

        //Act
        var places = ReplacementLibraryPlaces.Of(appPath, folders);

        //Assert
        Assert.Empty(places);
    }

    private static NormalizedPath Parsed(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _) ? parsed : throw new ArgumentException($"Not a path: {path}", nameof(path));
}
