using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-33 — TMDb's rate limit as the adapter keeps to it: requests paced 25 ms apart across every caller,
// a 429 waited out (its Retry-After) and the same GET sent again at most three times, then refused with a
// sentence. The pacing gate's own rules are TmdbRequestPacerTests'.
public sealed class TmdbAdapterRateLimitTests : IDisposable
{
    private const string TooManyRequestsError =
        "TMDb is asking the app to slow down (429 Too Many Requests), and still was after three retries. Try again in a minute.";

    // MAM-46 — the language an upgraded installation's TMDb keeps, which every exact URL below was recorded with.
    private const string CatalogLanguage = "uk-UA";

    private static readonly IntegrationConnectionSettings Settings = new(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token, CatalogLanguage);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTmdbServer _tmdb;

    public TmdbAdapterRateLimitTests()
    {
        _tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 679, "Чужі", "Aliens", "1986-07-18"));
    }

    private IMetadataCatalog Catalog => _harness.Adapter<IMetadataCatalog>(TmdbAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_429_is_waited_out_and_the_same_get_is_sent_again()
    {
        //Arrange
        var answered = 0;
        _tmdb.Intercept = _ => Interlocked.Increment(ref answered) == 1 ? TooManyRequests("0") : null;

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "679", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("Чужі", result.Value!.Summary.Title);
        Assert.Collection(
            _harness.TmdbHttp.Requests,
            first => Assert.Equal(
                $"{FakeTmdbServer.BaseUrl}/3/movie/679?append_to_response=credits,recommendations,alternative_titles,external_ids&language=uk-UA",
                first.Uri.AbsoluteUri),
            second =>
            {
                // The same request again, the token in its header.
                Assert.Equal(_harness.TmdbHttp.Requests[0].Uri, second.Uri);
                Assert.Equal([$"Bearer {FakeTmdbServer.Token}"], second.HeaderValues("Authorization"));
            });
    }

    [Fact]
    public async Task A_429_waits_the_seconds_its_retry_after_asks_for()
    {
        //Arrange
        var sentAt = new ConcurrentQueue<long>();
        _tmdb.Intercept = _ =>
        {
            sentAt.Enqueue(Stopwatch.GetTimestamp());
            return sentAt.Count == 1 ? TooManyRequests("1") : null;
        };

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "679", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var (first, second) = (sentAt.ElementAt(0), sentAt.ElementAt(1));
        // Not before the second TMDb asked for has passed since its answer, which came after the first was sent.
        Assert.True(Stopwatch.GetElapsedTime(first, second) >= TimeSpan.FromSeconds(1), $"Sent again after {Stopwatch.GetElapsedTime(first, second)}.");
    }

    [Fact]
    public async Task A_429_four_times_is_refused_after_three_retries()
    {
        //Arrange
        _tmdb.Intercept = _ => TooManyRequests("0");

        //Act
        var result = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "679", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(TooManyRequestsError, result.Error);
        Assert.Equal(4, _harness.TmdbHttp.Requests.Count);
        Assert.Single(_harness.TmdbHttp.Requests.Select(request => request.Uri).Distinct());
    }

    [Fact]
    public async Task A_429_after_the_last_retry_holds_back_the_next_caller_too()
    {
        //Arrange
        var sentAt = new ConcurrentQueue<long>();
        // Three 429s waited out at once, then a fourth asking for a second.
        _tmdb.Intercept = _ =>
        {
            sentAt.Enqueue(Stopwatch.GetTimestamp());
            return sentAt.Count switch
            {
                <= 3 => TooManyRequests("0"),
                4 => TooManyRequests("1"),
                _ => null,
            };
        };
        var cancellationToken = TestContext.Current.CancellationToken;
        var refused = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "679", cancellationToken);

        //Act
        var next = await Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "679", cancellationToken);

        //Assert
        // MAM-33 — the call that gave up still tells every caller to wait: TMDb counts requests per address, and the next
        // request from anyone would be over its limit too.
        Assert.Equal((IntegrationCallStatus.Refused, IntegrationCallStatus.Succeeded), (refused.Status, next.Status));
        var (lastRefused, sentNext) = (sentAt.ElementAt(3), sentAt.ElementAt(4));
        Assert.True(
            Stopwatch.GetElapsedTime(lastRefused, sentNext) >= TimeSpan.FromSeconds(1),
            $"The next caller's request was sent {Stopwatch.GetElapsedTime(lastRefused, sentNext)} after the last 429.");
    }

    [Fact]
    public async Task A_429_to_the_connection_test_and_to_a_poster_is_waited_out_too()
    {
        //Arrange
        var answered = 0;
        _tmdb.Intercept = _ => Interlocked.Increment(ref answered) % 2 == 1 ? TooManyRequests("0") : null;
        var adapter = _harness.Adapter(TmdbAdapter.TypeId);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var test = await adapter.TestConnectionAsync(Settings, cancellationToken);
        var poster = await Catalog.GetPosterAsync(Settings, "/poster.jpg", cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, test.Status);
        Assert.Equal(IntegrationCallStatus.Succeeded, poster.Status);
        Assert.Equal(4, _harness.TmdbHttp.Requests.Count);
        // The poster's retry carries no token either.
        Assert.All(
            _harness.TmdbHttp.Requests.Where(request => request.Uri.Host == FakeTmdbServer.ImageHost),
            request => Assert.Empty(request.HeaderValues("Authorization")));
    }

    [Fact]
    public async Task A_429_four_times_to_a_poster_is_refused()
    {
        //Arrange
        _tmdb.Intercept = _ => TooManyRequests("0");

        //Act
        var result = await Catalog.GetPosterAsync(Settings, "/poster.jpg", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(TooManyRequestsError, result.Error);
        Assert.Equal(4, _harness.TmdbHttp.Requests.Count);
    }

    [Fact]
    public async Task Requests_of_callers_at_once_start_at_least_25_ms_apart()
    {
        //Arrange
        var sentAt = new ConcurrentQueue<long>();
        _tmdb.Intercept = _ =>
        {
            sentAt.Enqueue(Stopwatch.GetTimestamp());
            return null;
        };
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Stopwatch.GetTimestamp();

        //Act
        // Five callers of different contracts at once: one gate paces them all.
        var work = Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "679", cancellationToken);
        var sameWork = Catalog.GetWorkAsync(Settings, MediaWorkKind.Movie, "679", cancellationToken);
        var poster = Catalog.GetPosterAsync(Settings, "/poster.jpg", cancellationToken);
        var lookup = _harness.Adapter<IMetadataResolver>(TmdbAdapter.TypeId)
            .LookupAsync(Settings, new MetadataQuery(MediaWorkKind.Movie, [], "Aliens", 1986), cancellationToken);
        var genres = Catalog.GetGenresAsync(Settings, MediaWorkKind.Movie, cancellationToken);
        await Task.WhenAll(work, sameWork, poster, lookup, genres);

        //Assert
        Assert.All(
            new[] { (await work).Status, (await sameWork).Status, (await poster).Status, (await lookup).Status, (await genres).Status },
            status => Assert.Equal(IntegrationCallStatus.Succeeded, status));
        Assert.Equal(5, sentAt.Count);
        // Each request starts its turn 25 ms after the one before it, so the fifth is sent at least four
        // turns after the first could start.
        var last = sentAt.Max();
        Assert.True(
            Stopwatch.GetElapsedTime(started, last) >= 4 * TmdbRequestPacer.MinimumSpacing,
            $"The fifth request was sent {Stopwatch.GetElapsedTime(started, last)} after the first could start.");
    }

    private static HttpResponseMessage TooManyRequests(string retryAfterSeconds)
    {
        // What TMDb answers over its limit.
        var response = VendorResponses.Text(
            """{"success":false,"status_code":25,"status_message":"Your request count (#) is over the allowed limit of (40)."}""",
            HttpStatusCode.TooManyRequests,
            "application/json");
        response.Headers.TryAddWithoutValidation("Retry-After", retryAfterSeconds);
        return response;
    }
}
