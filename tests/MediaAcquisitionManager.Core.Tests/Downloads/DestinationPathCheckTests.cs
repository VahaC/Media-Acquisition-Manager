using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-11, ADR 0003 — where a destination saves, checked against the media library folders: the client's
// folder, mapped through the client's path mappings, inside a library folder (folder by folder, the folder
// itself included) or a status saying why not — never a guess. Every status is pinned with its sentence,
// on the reference installation's samples and on hand-made cases for letter case, parents of a library
// and Windows paths.
public sealed class DestinationPathCheckTests
{
    // The reference installation (ADR 0003): qBittorrent sees the media folder as /downloads, the app and
    // Jellyfin as /media.
    private static readonly PathMappingPair DownloadsToMedia = new("/downloads", "/media");

    // The root folders of VendorSamples/Jellyfin/virtual-folders.json; Jellyfin sees them where the app
    // does, so they are already the app's paths.
    private static readonly LibraryFolder[] SampleFolders =
    [
        new("Фільми", "/media/movies"),
        new("Мульфільми", "/media/cartoons"),
        new("Документалки", "/media/documentary"),
        new("Колекції", "/config/data/collections"),
        new("Серіали", "/media/tv-shows"),
    ];

    private const string ConsequenceEnding = ", so automatic replacement stays off for this destination.";
    private const string ServicePathUnknownError = "The client doesn't say which folder this category saves to" + ConsequenceEnding;
    private const string LibrariesUnknownError =
        "The app doesn't know the media library folders yet. Sync a library from the Library page, then look again.";

    [Fact]
    public async Task Check_of_the_adr_0003_samples_puts_every_qbittorrent_category_in_its_jellyfin_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var categories = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryPaths.VendorSamples, "QBittorrent", "categories.json"), cancellationToken));
        using var libraries = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryPaths.VendorSamples, "Jellyfin", "virtual-folders.json"), cancellationToken));
        // Keyed by name, as qBittorrent lists them; savePath is each one's folder.
        List<ClientDestination> destinations =
        [
            .. categories.RootElement.EnumerateObject().Select(category => new ClientDestination(
                category.Name, category.Value.GetProperty("name").GetString()!, category.Value.GetProperty("savePath").GetString())),
        ];
        // The Jellyfin integration has no mappings, so each folder maps onto itself.
        List<LibraryFolder> folders =
        [
            .. libraries.RootElement.EnumerateArray().SelectMany(library => library.GetProperty("Locations").EnumerateArray()
                .Select(location => new LibraryFolder(library.GetProperty("Name").GetString()!, PathMapper.Map(location.GetString()!, []).AppPath!))),
        ];

        //Act
        var results = destinations.Select(destination => DestinationPathCheck.Check(destination.Key, destinations, [DownloadsToMedia], folders)).ToList();

        //Assert
        Assert.Equal(
            [
                "InLibrary /downloads/cartoons → /media/cartoons [Мульфільми] ∅",
                "InLibrary /downloads/documentary → /media/documentary [Документалки] ∅",
                "InLibrary /downloads/movies → /media/movies [Фільми] ∅",
                "InLibrary /downloads/tv-shows → /media/tv-shows [Серіали] ∅",
            ],
            results.Select(Describe));
    }

    [Fact]
    public void Check_by_key_checks_the_destination_with_exactly_that_key()
    {
        //Arrange
        ClientDestination[] destinations = [new("Movies", "Movies", "/downloads/movies"), new("Cartoons", "Cartoons", "/downloads/cartoons")];

        //Act
        var result = DestinationPathCheck.Check("Cartoons", destinations, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal("InLibrary /downloads/cartoons → /media/cartoons [Мульфільми] ∅", Describe(result));
    }

    [Theory]
    [InlineData("Movies 4K")]
    // Exactly the client's name: another letter case or a space is another category.
    [InlineData("movies")]
    [InlineData("Movies ")]
    [InlineData(" Movies")]
    public void Check_by_key_of_a_key_the_client_has_not_got_is_category_missing(string key)
    {
        //Arrange
        ClientDestination[] destinations = [new("Movies", "Movies", "/downloads/movies")];

        //Act
        var result = DestinationPathCheck.Check(key, destinations, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal($"CategoryMissing ∅ → ∅ [] The client has no category “{key}” any more.", Describe(result));
    }

    [Fact]
    public void Check_by_key_of_a_client_without_destinations_is_category_missing()
    {
        //Arrange

        //Act
        var result = DestinationPathCheck.Check("Movies", [], [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal(DestinationPathStatus.CategoryMissing, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_of_a_destination_whose_client_does_not_say_where_is_path_unknown(string? servicePath)
    {
        //Arrange
        ClientDestination destination = new("Movies", "Movies", servicePath);

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal($"PathUnknown ∅ → ∅ [] {ServicePathUnknownError}", Describe(result));
    }

    [Fact]
    public void Check_of_a_folder_no_mapping_covers_is_path_unknown_with_the_mappers_reason()
    {
        //Arrange
        ClientDestination destination = new("Incoming", "Incoming", "/incoming/movies");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        // Identity is no fallback once the owner has mapped the client's paths.
        Assert.Equal(
            $"PathUnknown /incoming/movies → ∅ [] No path mapping of this integration covers “/incoming/movies”{ConsequenceEnding}",
            Describe(result));
    }

    [Fact]
    public void Check_of_a_folder_the_mappings_send_to_two_places_is_path_unknown()
    {
        //Arrange
        ClientDestination destination = new("Movies", "Movies", "/downloads/movies");
        PathMappingPair[] pairs = [DownloadsToMedia, new("/downloads/movies", "/data/movies")];

        //Act
        var result = DestinationPathCheck.Check(destination, pairs, SampleFolders);

        //Assert
        // Never settled by the longer prefix, which would land it in "Фільми" by a guess.
        Assert.Equal(
            "PathUnknown /downloads/movies → ∅ [] The path mappings of this integration send “/downloads/movies” to different places: " +
                $"“/media/movies” and “/data/movies”{ConsequenceEnding}",
            Describe(result));
    }

    [Theory]
    [InlineData("downloads/movies")]
    [InlineData("/downloads/../media/movies")]
    public void Check_of_a_folder_that_is_not_a_full_path_is_path_unknown(string servicePath)
    {
        //Arrange
        ClientDestination destination = new("Movies", "Movies", servicePath);

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal($"PathUnknown {servicePath} → ∅ [] “{servicePath}” is not a full path the app can map{ConsequenceEnding}", Describe(result));
    }

    [Fact]
    public void Check_without_library_folders_is_libraries_unknown_with_both_paths()
    {
        //Arrange
        ClientDestination destination = new("Movies", "Movies", "/downloads/movies");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], []);

        //Assert
        Assert.Equal($"LibrariesUnknown /downloads/movies → /media/movies [] {LibrariesUnknownError}", Describe(result));
    }

    [Theory]
    [InlineData(null, ServicePathUnknownError)]
    [InlineData("/incoming", "No path mapping of this integration covers “/incoming”" + ConsequenceEnding)]
    public void Check_says_the_folder_is_unknown_before_it_says_the_libraries_are(string? servicePath, string expected)
    {
        //Arrange
        ClientDestination destination = new("Movies", "Movies", servicePath);

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], []);

        //Assert
        // Nothing would follow from syncing a library while the folder itself is unknown.
        Assert.Equal((DestinationPathStatus.PathUnknown, expected), (result.Status, result.Problem));
    }

    [Fact]
    public void Check_of_a_folder_outside_every_library_is_outside_libraries()
    {
        //Arrange
        ClientDestination destination = new("Incoming", "Incoming", "/downloads/incoming");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal(
            $"OutsideLibraries /downloads/incoming → /media/incoming [] “/media/incoming” is outside every media library folder{ConsequenceEnding}",
            Describe(result));
    }

    [Fact]
    public void Check_of_the_parent_of_library_folders_is_outside_libraries()
    {
        //Arrange
        // /media holds /media/movies, but what lands in /media itself is in no library.
        ClientDestination destination = new("Everything", "Everything", "/downloads");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal(
            $"OutsideLibraries /downloads → /media [] “/media” is outside every media library folder{ConsequenceEnding}",
            Describe(result));
    }

    [Fact]
    public void Check_of_a_library_folder_itself_is_in_that_library()
    {
        //Arrange
        ClientDestination destination = new("Movies", "Movies", "/downloads/movies/");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        // Equality counts; the path is kept as the client said it, the app's in canonical form.
        Assert.Equal("InLibrary /downloads/movies/ → /media/movies [Фільми] ∅", Describe(result));
    }

    [Fact]
    public void Check_of_a_folder_deep_inside_a_library_is_in_it()
    {
        //Arrange
        ClientDestination destination = new("Movies 4K", "Movies 4K", "/downloads/movies/4K/UHD");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal("InLibrary /downloads/movies/4K/UHD → /media/movies/4K/UHD [Фільми] ∅", Describe(result));
    }

    [Fact]
    public void Check_matches_whole_folder_names_only()
    {
        //Arrange
        ClientDestination destination = new("Movies 2", "Movies 2", "/downloads/movies2");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal(DestinationPathStatus.OutsideLibraries, result.Status);
    }

    [Fact]
    public void Check_compares_posix_paths_with_their_letter_case()
    {
        //Arrange
        // ext4: /media/Movies is not /media/movies.
        ClientDestination destination = new("Movies", "Movies", "/downloads/Movies");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], SampleFolders);

        //Assert
        Assert.Equal(
            $"OutsideLibraries /downloads/Movies → /media/Movies [] “/media/Movies” is outside every media library folder{ConsequenceEnding}",
            Describe(result));
    }

    [Fact]
    public void Check_maps_a_windows_clients_folder_in_any_letter_case()
    {
        //Arrange
        // qBittorrent on Windows, the app and Jellyfin in containers.
        ClientDestination destination = new("Movies", "Movies", @"d:/torrents/movies");
        PathMappingPair[] pairs = [new(@"D:\Torrents", "/media")];

        //Act
        var result = DestinationPathCheck.Check(destination, pairs, SampleFolders);

        //Assert
        Assert.Equal("InLibrary d:/torrents/movies → /media/movies [Фільми] ∅", Describe(result));
    }

    [Fact]
    public void Check_compares_windows_folders_in_any_letter_case()
    {
        //Arrange
        // Everything on one Windows machine: no mappings, so the client's folder is the app's.
        ClientDestination destination = new("Movies", "Movies", @"d:\media\MOVIES\4k");
        LibraryFolder[] folders = [new("Movies", @"D:\Media\Movies"), new("Shows", @"D:\Media\Shows")];

        //Act
        var result = DestinationPathCheck.Check(destination, [], folders);

        //Assert
        Assert.Equal(@"InLibrary d:\media\MOVIES\4k → D:\media\MOVIES\4k [Movies] ∅", Describe(result));
    }

    [Fact]
    public void Check_of_a_posix_folder_is_never_inside_a_windows_library_folder()
    {
        //Arrange
        ClientDestination destination = new("Movies", "Movies", "/media/movies");
        LibraryFolder[] folders = [new("Movies", @"D:\media\movies")];

        //Act
        var result = DestinationPathCheck.Check(destination, [], folders);

        //Assert
        Assert.Equal(DestinationPathStatus.OutsideLibraries, result.Status);
    }

    [Fact]
    public void Check_without_mappings_takes_the_clients_folder_as_the_apps()
    {
        //Arrange
        ClientDestination destination = new("Movies", "Movies", "/media/movies");

        //Act
        var result = DestinationPathCheck.Check(destination, [], SampleFolders);

        //Assert
        Assert.Equal("InLibrary /media/movies → /media/movies [Фільми] ∅", Describe(result));
    }

    [Fact]
    public void Check_names_every_library_that_holds_the_folder_once_in_the_order_of_its_first_folder()
    {
        //Arrange
        // Nested roots: unlike a file's one library, a destination may sit in several, and each is said.
        LibraryFolder[] folders =
        [
            new("Фільми", "/media/movies"),
            new("Усе", "/media"),
            new("Мульфільми", "/media/cartoons"),
            new("Фільми", "/media/movies/4K"),
            new("Усе", "/media/movies"),
        ];
        ClientDestination destination = new("Movies 4K", "Movies 4K", "/downloads/movies/4K");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], folders);

        //Assert
        Assert.Equal("InLibrary /downloads/movies/4K → /media/movies/4K [Фільми, Усе] ∅", Describe(result));
    }

    [Fact]
    public void Check_skips_a_library_folder_it_can_not_parse()
    {
        //Arrange
        LibraryFolder[] folders = [new("Broken", "movies"), new("Broken", "/media/../media"), new("Фільми", "/media/movies")];
        ClientDestination destination = new("Movies", "Movies", "/downloads/movies");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], folders);

        //Assert
        Assert.Equal("InLibrary /downloads/movies → /media/movies [Фільми] ∅", Describe(result));
    }

    [Fact]
    public void Check_with_only_folders_it_can_not_parse_is_outside_libraries()
    {
        //Arrange
        // Folders were given, so the libraries are known; they only hold nothing.
        LibraryFolder[] folders = [new("Broken", "movies")];
        ClientDestination destination = new("Movies", "Movies", "/downloads/movies");

        //Act
        var result = DestinationPathCheck.Check(destination, [DownloadsToMedia], folders);

        //Assert
        Assert.Equal(DestinationPathStatus.OutsideLibraries, result.Status);
    }

    [Fact]
    public void ClientDisabled_says_only_the_given_sentence()
    {
        //Arrange
        const string problem = "“qBittorrent” is switched off. Switch it on under Integrations to check this destination.";

        //Act
        var result = DestinationPathCheckResult.ClientDisabled(problem);

        //Assert
        // Nothing was asked, so nothing is known about the folder.
        Assert.Equal($"ClientDisabled ∅ → ∅ [] {problem}", Describe(result));
    }

    [Fact]
    public void ClientUnavailable_says_only_the_given_sentence()
    {
        //Arrange
        const string problem = "qBittorrent answered 503 Service Unavailable. Its own log says why.";

        //Act
        var result = DestinationPathCheckResult.ClientUnavailable(problem);

        //Assert
        Assert.Equal($"ClientUnavailable ∅ → ∅ [] {problem}", Describe(result));
    }

    // "<status> <service path> → <app path> [<libraries>] <problem>", "∅" for null.
    private static string Describe(DestinationPathCheckResult result) =>
        $"{result.Status} {result.ServicePath ?? "∅"} → {result.AppPath ?? "∅"} [{string.Join(", ", result.LibraryNames)}] {result.Problem ?? "∅"}";
}
