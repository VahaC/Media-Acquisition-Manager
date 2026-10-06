using System.IO.Pipelines;
using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-34 — what every test of Jellyfin's Identify (<see cref="IMediaLibraryItemIdentifier"/>) stands on: the real adapter
/// behind an <see cref="IntegrationAdapterHarness"/>, and a <see cref="FakeJellyfinServer"/> serving the three items the live
/// check of 2026-10-06 identified — the film "120. Landing Stripling" and the series The Penguins of Madagascar, both under
/// <see cref="JellyfinSamples.CartoonsRoot"/>, and the episode Jellyfin lists as a film, Blitzkrieg, under
/// <see cref="JellyfinSamples.DocumentaryRoot"/> — each with the search result Jellyfin's provider answered for it, recorded
/// (<see cref="JellyfinSamples.RemoteSearchMovieByTmdbRows"/> and its siblings).
/// </summary>
/// <remarks>
/// Every test class makes its own: the tests of the adapter are split by what they prove, and what they stand on is one place.
/// Nothing of it hides behaviour — the server's items and results are exposed, and a test changes what it needs before it asks.
/// </remarks>
public sealed class JellyfinIdentifyHarness : IDisposable
{
    /// <summary>The address the fake Jellyfin answers at.</summary>
    public const string BaseUrl = "http://jellyfin.test:8096";

    /// <summary>The API key every request must carry.</summary>
    public const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    /// <summary>The connection every identification is made with.</summary>
    public static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    /// <param name="timeout">A client timeout in place of the app's 15 seconds and 20 minutes, so a timeout test takes
    /// milliseconds.</param>
    public JellyfinIdentifyHarness(TimeSpan? timeout = null)
    {
        Adapters = new IntegrationAdapterHarness(timeout: timeout);
        Server = new FakeJellyfinServer(BaseUrl) { ApiKey = ApiKey };
        // The three items the live check read, beside the sample movies the sync reads.
        Server.Movies.Add(JellyfinSamples.Item());
        Server.Series.Add(JellyfinSamples.SeriesItem());
        Server.Movies.Add(JellyfinSamples.EpisodeMovieItem());
        // What each provider answered for them, as recorded.
        Server.Matches.Add(new FakeJellyfinMatch("Movie", FakeJellyfinServer.TmdbProvider, "Tmdb", JellyfinSamples.LandingStriplingTmdbId, JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject()));
        Server.Matches.Add(new FakeJellyfinMatch("Series", FakeJellyfinServer.TmdbProvider, "Tmdb", JellyfinSamples.PenguinsTmdbTvId, JellyfinSamples.RemoteSearchSeriesByTmdbRows()[0]!.AsObject()));
        Server.Matches.Add(new FakeJellyfinMatch("Movie", FakeJellyfinServer.OmdbProvider, "Imdb", JellyfinSamples.BlitzkriegImdbId, JellyfinSamples.RemoteSearchOmdbRows()[0]!.AsObject()));
        Server.Arm(Adapters.JellyfinHttp);
    }

    /// <summary>The real adapters, registered as the app registers them.</summary>
    public IntegrationAdapterHarness Adapters { get; }

    /// <summary>The fake Jellyfin.</summary>
    public FakeJellyfinServer Server { get; }

    /// <summary>The network behind both Jellyfin clients: every request of an identification, in order.</summary>
    public RecordingHttpHandler Http => Adapters.JellyfinHttp;

    /// <summary>Jellyfin's adapter as the capability, found the way the app finds it.</summary>
    public IMediaLibraryItemIdentifier Identifier => Adapters.Adapter<IMediaLibraryItemIdentifier>(JellyfinAdapter.TypeId);

    /// <summary>The film as the fake holds it: "120. Landing Stripling", no ids, under the cartoon library.</summary>
    public JsonObject Film => Stored(Server.Movies, JellyfinSamples.LandingStriplingId);

    /// <summary>The series as the fake holds it: The Penguins of Madagascar named after its folder, no ids, under the
    /// cartoon library.</summary>
    public JsonObject Show => Stored(Server.Series, JellyfinSamples.PenguinsId);

    /// <summary>The episode Jellyfin lists as a film, as the fake holds it: Blitzkrieg named after its file, year 2025, its
    /// IMDb id, under the documentary library.</summary>
    public JsonObject EpisodeMovie => Stored(Server.Movies, JellyfinSamples.BlitzkriegId);

    /// <summary>A film's request: its TMDb id, the owner's own word unless <paramref name="protectHeldIds"/>.</summary>
    public static LibraryIdentifyRequest FilmRequest(bool protectHeldIds = false) =>
        new(JellyfinSamples.LandingStriplingId, MediaWorkKind.Movie, new MediaIdentifier(MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId), protectHeldIds);

    /// <summary>A show's request: its TMDb id.</summary>
    public static LibraryIdentifyRequest ShowRequest(bool protectHeldIds = false) =>
        new(JellyfinSamples.PenguinsId, MediaWorkKind.Series, new MediaIdentifier(MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId), protectHeldIds);

    /// <summary>An episode listed as a film: the episode's own IMDb id.</summary>
    public static LibraryIdentifyRequest EpisodeMovieRequest(bool protectHeldIds = false) =>
        new(JellyfinSamples.BlitzkriegId, MediaWorkKind.Movie, new MediaIdentifier(MediaIdentifierScheme.Imdb, JellyfinSamples.BlitzkriegImdbId), protectHeldIds);

    /// <summary>The identification of <paramref name="request"/> with the harness's connection.</summary>
    public Task<IntegrationCallResult<LibraryIdentification>> IdentifyAsync(LibraryIdentifyRequest request, CancellationToken cancellationToken) =>
        Identifier.IdentifyAsync(Settings, request, cancellationToken);

    /// <summary>Every request received, oldest first, as <c>METHOD /path?query</c>.</summary>
    public List<string> Lines() => [.. Http.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>The requests that were Apply.</summary>
    public List<RecordedHttpRequest> ApplyRequests() =>
        [.. Http.Requests.Where(request => request.Method == HttpMethod.Post && request.Uri.AbsolutePath.Contains("/RemoteSearch/Apply/", StringComparison.Ordinal))];

    /// <summary>The requests that were a search.</summary>
    public List<RecordedHttpRequest> SearchRequests() =>
        [.. Http.Requests.Where(request =>
            request.Method == HttpMethod.Post
            && (request.Uri.AbsolutePath.EndsWith("/RemoteSearch/Movie", StringComparison.Ordinal)
                || request.Uri.AbsolutePath.EndsWith("/RemoteSearch/Series", StringComparison.Ordinal)))];

    /// <summary>A copy of the film under <paramref name="root"/>, as another item: its own id, its path and its media
    /// source's replaced, added to the fake's movies and returned.</summary>
    public JsonObject AddFilmUnder(string root, string itemId)
    {
        var path = $"{root}/Some Film (1962)/Some Film (1962).mkv";
        var film = JellyfinSamples.Item();
        film["Id"] = itemId;
        film["Path"] = path;
        film["MediaSources"]![0]!["Id"] = itemId;
        film["MediaSources"]![0]!["Path"] = path;
        Server.Movies.Add(film);
        return film;
    }

    /// <summary>The Penguins series as another item under <paramref name="root"/>: its own id and path.</summary>
    public JsonObject AddShowUnder(string root, string itemId)
    {
        var show = JellyfinSamples.SeriesItem();
        show["Id"] = itemId;
        show["Path"] = $"{root}/Some Show";
        Server.Series.Add(show);
        return show;
    }

    /// <summary>Each id of <paramref name="identifiers"/> as <c>Scheme value</c>.</summary>
    public static List<string> Describe(IReadOnlyList<MediaIdentifier> identifiers) =>
        [.. identifiers.Select(identifier => $"{identifier.Scheme} {identifier.Value}")];

    /// <summary>A 200 whose body never finishes arriving: the pipe is never written to or completed.</summary>
    public static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };

    /// <summary>
    /// What <see cref="SocketsHttpHandler"/> itself throws when its connect timeout runs out: the runtime's own exception
    /// rather than a copy built by hand, so a test follows .NET if the shape changes.
    /// </summary>
    public static async Task<Exception> ConnectTimeoutExceptionAsync(CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(50),
            // A connection nothing ever accepts: the attempt waits until the connect timeout ends it.
            ConnectCallback = async (_, connectCancellation) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, connectCancellation);
                return Stream.Null;
            },
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://jellyfin.test:8096/System/Info");
        try
        {
            await invoker.SendAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The connection was accepted.");
    }

    /// <summary>A 200 with <paramref name="body"/> as JSON.</summary>
    public static HttpResponseMessage JsonAnswer(string body) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

    public void Dispose() => Adapters.Dispose();

    private static JsonObject Stored(List<JsonObject> items, string id) =>
        items.Single(item => string.Equals((string?)item["Id"], id, StringComparison.Ordinal));
}
