using System.Globalization;
using System.Text.Json.Nodes;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// The real Jellyfin answers of <c>VendorSamples/Jellyfin</c> (MAM-6) as editable JSON, and variants
/// synthesized from them for what the reference library doesn't show (MAM-8): merged versions, a
/// library of any size, a movie without metadata, a stacked movie's parts.
/// </summary>
/// <remarks>
/// Every call returns fresh nodes, parsed anew, so a test can change what it got (a path, a media
/// source's protocol, a provider id) without touching another test's copy.
/// </remarks>
public static class JellyfinSamples
{
    /// <summary>"The Continental" S01E01: an episode in the movie library, in a season folder inside
    /// the <c>John Wick. Collection</c> folder, with an IMDb id only.</summary>
    public const string ContinentalId = "0d6e8ae1b5acb7a98486f5dfc099d94c";

    /// <summary>"13-й район" (2004) in the <c>District 13. Dilogy</c> folder: IMDb, TMDb and a TMDb
    /// collection id.</summary>
    public const string District13Id = "e3d063df4b73d12c0d4cd71725e5e3eb";

    /// <summary>"Чужі" (1986), the theatrical cut, in the <c>Alien. Collection</c> folder: IMDb tt0090605,
    /// TMDb 679.</summary>
    public const string AliensTheatricalId = "00a49b2db2200514168431dc028f0a0b";

    /// <summary>"Чужі" (1986), the special edition: a second item with the same ids as the theatrical
    /// cut.</summary>
    public const string AliensSpecialEditionId = "436a68097ee85c654c2e696bcf2fa8d6";

    /// <summary>The library "Фільми", rooted at <c>/media/movies</c>, which holds all four movies.</summary>
    public const string MoviesLocationId = "78ee018b5034640ba414833f0fe9d6b3";

    /// <summary>The total <c>movies-page.json</c> reports: the reference library's 870 movies, not the
    /// four in the file.</summary>
    public const int SampleTotalRecordCount = 870;

    private static readonly Lazy<string> MoviesPageText = new(() => Read("movies-page.json"));
    private static readonly Lazy<string> SystemInfoText = new(() => Read("system-info.json"));
    private static readonly Lazy<string> VirtualFoldersText = new(() => Read("virtual-folders.json"));

    /// <summary>The answer of <c>GET /System/Info</c>: Jellyfin 12.1.0, "VahaC media".</summary>
    public static JsonObject SystemInfo() => JsonNode.Parse(SystemInfoText.Value)!.AsObject();

    /// <summary>The answer of <c>GET /Library/VirtualFolders</c>: five libraries.</summary>
    public static JsonArray VirtualFolders() => JsonNode.Parse(VirtualFoldersText.Value)!.AsArray();

    /// <summary>The four movies of <c>movies-page.json</c>, in its order: The Continental, 13-й район,
    /// then the two "Чужі".</summary>
    public static List<JsonObject> Movies()
    {
        var items = JsonNode.Parse(MoviesPageText.Value)!["Items"]!.AsArray();
        var movies = items.Select(item => item!.AsObject()).ToList();
        // Detached from the parsed page, so each can go into another answer.
        items.Clear();
        return movies;
    }

    /// <summary>The sample movie <paramref name="id"/>.</summary>
    public static JsonObject Movie(string id) => Movies().Single(movie => string.Equals((string?)movie["Id"], id, StringComparison.Ordinal));

    /// <summary>
    /// The theatrical "Чужі" listing the special edition's media source as well, labelled by its name:
    /// what Jellyfin 12.1 answers for two versions it merged into one item.
    /// </summary>
    public static JsonObject AliensWithBothSources()
    {
        var theatrical = Movie(AliensTheatricalId);
        theatrical["MediaSources"]!.AsArray().Add(Movie(AliensSpecialEditionId)["MediaSources"]![0]!.DeepClone());
        return theatrical;
    }

    /// <summary>
    /// Both "Чужі" items, each listing both media sources in the same order: how Jellyfin 10.9 to 10.11
    /// answer for two versions they merged.
    /// </summary>
    public static List<JsonObject> AliensEachListingBothSources()
    {
        var theatrical = Movie(AliensTheatricalId);
        var specialEdition = Movie(AliensSpecialEditionId);
        var theatricalSource = theatrical["MediaSources"]![0]!;
        var specialEditionSource = specialEdition["MediaSources"]![0]!;
        theatrical["MediaSources"]!.AsArray().Add(specialEditionSource.DeepClone());
        specialEdition["MediaSources"]!.AsArray().Insert(0, theatricalSource.DeepClone());
        return [theatrical, specialEdition];
    }

    /// <summary>A copy of <paramref name="movie"/> without provider ids: Jellyfin found no metadata for
    /// it.</summary>
    public static JsonObject WithoutProviderIds(JsonObject movie)
    {
        var copy = movie.DeepClone().AsObject();
        copy.Remove("ProviderIds");
        return copy;
    }

    /// <summary>
    /// A copy of <paramref name="movie"/> as another movie, number <paramref name="number"/> (1 or more):
    /// its id, its media sources' ids, its paths and its IMDb and TMDb ids renumbered, so it is another
    /// item with another file and other identifiers. Copies of one number of the two "Чужі" items still
    /// share their identifiers, like the originals.
    /// </summary>
    public static JsonObject Renumbered(JsonObject movie, int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        var copy = movie.DeepClone().AsObject();
        var prefix = number.ToString("x8", CultureInfo.InvariantCulture);
        copy["Id"] = RenumberId(copy["Id"]!.GetValue<string>(), prefix);
        if (copy["Path"] is JsonValue path)
            copy["Path"] = RenumberPath(path.GetValue<string>(), number);

        if (copy["MediaSources"] is JsonArray sources)
        {
            foreach (var source in sources.OfType<JsonObject>())
            {
                if (source["Id"] is JsonValue sourceId)
                    source["Id"] = RenumberId(sourceId.GetValue<string>(), prefix);
                if (source["Path"] is JsonValue sourcePath)
                    source["Path"] = RenumberPath(sourcePath.GetValue<string>(), number);
            }
        }

        // Fixed-width prefixes, so no two numbers can make one id; the forms stay valid (tt and
        // digits, digits only).
        var digits = number.ToString("D6", CultureInfo.InvariantCulture);
        if (copy["ProviderIds"] is JsonObject providerIds)
        {
            if (providerIds["Imdb"] is JsonValue imdb)
                providerIds["Imdb"] = $"tt{digits}{imdb.GetValue<string>()[2..]}";
            if (providerIds["Tmdb"] is JsonValue tmdb)
                providerIds["Tmdb"] = $"{digits}{tmdb.GetValue<string>()}";
        }

        return copy;
    }

    /// <summary>
    /// A library of <paramref name="count"/> movies: renumbered copies of the four samples in turn, the
    /// first four numbered 1, the next four 2, and so on. Every four movies make three works (the two
    /// "Чужі" of a number share their ids), four versions and four files.
    /// </summary>
    public static List<JsonObject> Library(int count)
    {
        var samples = Movies();
        return [.. Enumerable.Range(0, count).Select(index => Renumbered(samples[index % samples.Count], index / samples.Count + 1))];
    }

    /// <summary>
    /// A movie of its own with these facts (MAM-12): "13-й район" with its id, titles, year, provider ids
    /// and path replaced, for the titles and ids the reference library doesn't hold — a search page's
    /// "Інтерстеллар", a remake of another year. An original title or id that is <c>null</c> is left out,
    /// the way Jellyfin leaves out what it doesn't know.
    /// </summary>
    public static JsonObject MovieWith(
        string id, string title, string? originalTitle, int year, string path, string? imdbId = null, string? tmdbId = null)
    {
        var movie = Movie(District13Id);
        movie["Id"] = id;
        movie["Name"] = title;
        movie.Remove("OriginalTitle");
        if (originalTitle is not null)
            movie["OriginalTitle"] = originalTitle;
        movie["ProductionYear"] = year;
        var providerIds = new JsonObject();
        if (imdbId is not null)
            providerIds["Imdb"] = imdbId;
        if (tmdbId is not null)
            providerIds["Tmdb"] = tmdbId;
        movie["ProviderIds"] = providerIds;
        movie["Path"] = path;
        movie["MediaSources"]![0]!["Id"] = id;
        movie["MediaSources"]![0]!["Path"] = path;
        return movie;
    }

    /// <summary>
    /// One further part of a stacked movie as <c>GET /Videos/{id}/AdditionalParts</c> lists it: an item
    /// of its own with the part's path, and its size on its media source.
    /// </summary>
    public static JsonObject Part(string id, string path, long? sizeBytes) => new()
    {
        ["Id"] = id,
        ["Type"] = "Movie",
        ["Path"] = path,
        ["MediaSources"] = new JsonArray(new JsonObject
        {
            ["Protocol"] = "File",
            ["Id"] = id,
            ["Path"] = path,
            ["Size"] = sizeBytes,
            ["IsRemote"] = false,
            ["VideoType"] = "VideoFile",
        }),
    };

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryPaths.VendorSamples, "Jellyfin", fileName));

    private static string RenumberId(string id, string prefix) => id.Length > prefix.Length ? prefix + id[prefix.Length..] : prefix + id;

    // "…/Aliens (1986) … [Hurtom].mkv" becomes "…/Aliens (1986) … [Hurtom] #7.mkv".
    private static string RenumberPath(string path, int number)
    {
        var name = path.LastIndexOf('/') + 1;
        var extension = path.LastIndexOf('.');
        var at = extension > name ? extension : path.Length;
        return string.Create(CultureInfo.InvariantCulture, $"{path[..at]} #{number}{path[at..]}");
    }
}
