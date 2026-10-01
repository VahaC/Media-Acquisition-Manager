using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-10 — the owner's opt-in check of the adapter against his real feed, over the real network: the
// samples are a snapshot, and a gateway's answers change. Skipped unless LIVE_TORZNAB_URL (the feed's
// address, as entered in the app) and LIVE_TORZNAB_API_KEY are set — deliberately not MAM_-prefixed,
// which are the app's own settings. A fetch counts as a grab in the gateway, so it needs
// LIVE_TORZNAB_FETCH=1 as well.
//
// Every assertion takes booleans, counts and fixed messages only, and the summary written to the test
// output holds counts, status names and hosts: a failure printing a URL, a locator, a title or the
// feed's own error would put the owner's key or forum session into the TRX and JUnit files.
public sealed class TorznabLiveTests
{
    private const string UrlVariable = "LIVE_TORZNAB_URL";
    private const string ApiKeyVariable = "LIVE_TORZNAB_API_KEY";
    private const string FetchVariable = "LIVE_TORZNAB_FETCH";

    /// <summary>For <c>SkipUnless</c>: the feed's address and key are both set.</summary>
    public static bool HasLiveFeed =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVariable))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyVariable));

    /// <summary>For <c>SkipUnless</c>: the feed, and the owner's consent to a grab.</summary>
    public static bool HasLiveFetch =>
        HasLiveFeed && string.Equals(Environment.GetEnvironmentVariable(FetchVariable), "1", StringComparison.Ordinal);

    [Fact(Skip = "Set LIVE_TORZNAB_URL and LIVE_TORZNAB_API_KEY to search the owner's feed.", SkipUnless = nameof(HasLiveFeed))]
    public async Task Search_of_the_live_feed_finds_interstellar_through_the_app_s_own_plan()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(fakeNetwork: false);
        var search = harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);
        var settings = LiveSettings();
        var feed = new Uri(TorznabAdapter.EndpointFor(settings.BaseUrl));
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var capabilities = await search.GetCapabilitiesAsync(settings, cancellationToken);
        var (tier, tiers, answer) = await RunPlanAsync(search, settings, capabilities, cancellationToken);
        Report(capabilities, tier, tiers, answer);

        //Assert
        Assert.True(capabilities.IsSuccess, "The feed's capabilities call failed; the test output says how.");
        Assert.True(answer?.IsSuccess == true, "No tier of the plan searched successfully; the test output says how.");
        var hits = answer!.Value!;
        Assert.True(hits.Count >= 1, "No tier of the plan listed a release.");
        var movieCategories = capabilities.Value!.Categories.Where(category => category.HoldsMovies).Select(category => category.Id).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            hits.All(hit => hit.CategoryIds.Count == 0 || hit.CategoryIds.Any(id => IsMovieCategory(id, movieCategories))),
            "A release listed only outside the movie categories came back.");
        Assert.True(
            hits.All(hit => !HasSession(hit.SourcePageUrl) && !HasSession(hit.SourceItemId)),
            "A page link or an id kept a session.");
        Assert.True(
            hits.All(hit => !TorznabLinks.TryParseWebLink(hit.Locator.Value, out var link) || TorznabLinks.SameOrigin(link, feed)),
            "A download link points somewhere other than the feed's origin.");
    }

    [Fact(Skip = "Set LIVE_TORZNAB_URL, LIVE_TORZNAB_API_KEY and LIVE_TORZNAB_FETCH=1 to fetch from the owner's feed.", SkipUnless = nameof(HasLiveFetch))]
    public async Task Fetch_of_a_live_release_hands_over_a_torrent_or_a_magnet()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(fakeNetwork: false);
        var search = harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);
        var settings = LiveSettings();
        var cancellationToken = TestContext.Current.CancellationToken;
        var capabilities = await search.GetCapabilitiesAsync(settings, cancellationToken);
        var (_, _, answer) = await RunPlanAsync(search, settings, capabilities, cancellationToken);
        var locator = answer?.Value?.FirstOrDefault()?.Locator;

        //Act
        var payload = locator is null ? null : await search.FetchPayloadAsync(settings, locator, cancellationToken);
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine($"Fetch: {payload?.Status.ToString() ?? "not run, no release listed"}");
        if (payload?.Value is { } fetched)
            output?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Payload: {fetched.Kind}, {fetched.TorrentFile?.Length ?? 0} bytes."));

        //Assert
        Assert.True(locator is not null, "The search listed no release to fetch.");
        Assert.True(payload!.IsSuccess, "The fetch failed; the test output names its status.");
        Assert.True(
            payload.Value!.Kind is ReleasePayloadKind.TorrentFile ? payload.Value.TorrentFile is { Length: > 0 } : payload.Value.MagnetUri is not null,
            "The payload holds neither a .torrent file nor a magnet.");
    }

    /// <summary>
    /// The plan the Api runs: the tiers <see cref="ReleaseSearchPlanning"/> makes from the capabilities,
    /// in order, until one lists a release or fails.
    /// </summary>
    private static async Task<(int Tier, int Tiers, IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>? Answer)> RunPlanAsync(
        IReleaseSearchProvider search,
        IntegrationConnectionSettings settings,
        IntegrationCallResult<ReleaseSearchCapabilities> capabilities,
        CancellationToken cancellationToken)
    {
        if (!capabilities.IsSuccess)
            return (0, 0, null);

        var tiers = ReleaseSearchPlanning.For("Interstellar", 2014, [], capabilities.Value!);
        IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>? answer = null;
        for (var tier = 0; tier < tiers.Count; tier++)
        {
            answer = await search.SearchAsync(settings, tiers[tier], cancellationToken);
            if (!answer.IsSuccess || answer.Value!.Count > 0)
                return (tier + 1, tiers.Count, answer);
        }

        return (tiers.Count, tiers.Count, answer);
    }

    /// <summary>A summary without a secret: counts, status names, which tier answered, category ids and
    /// hosts — never a URL, a locator, a title, or an error before its addresses are cut to the host.</summary>
    private static void Report(
        IntegrationCallResult<ReleaseSearchCapabilities> capabilities, int tier, int tiers, IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>? answer)
    {
        var output = TestContext.Current.TestOutputHelper;
        if (output is null)
            return;

        output.WriteLine($"Capabilities: {capabilities.Status}{Failure(capabilities.Error)}");
        if (capabilities.Value is { } caps)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  text search {caps.HasTextSearch}, movie search {caps.HasMovieSearch} (text {caps.MovieSearchTakesText}, ids [{string.Join(", ", caps.MovieSearchIdentifierSchemes)}], year {caps.MovieSearchTakesYear}), limit {caps.ResultLimit?.ToString(CultureInfo.InvariantCulture) ?? "none"}, {caps.Categories.Count} categories, {caps.Categories.Count(category => category.HoldsMovies)} of them movies"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Search: tier {tier} of {tiers} answered {answer?.Status.ToString() ?? "nothing"}{Failure(answer?.Error)}"));
        if (answer?.Value is not { } hits)
            return;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {hits.Count} releases, {hits.Count(hit => hit.PayloadKind is not null)} with a known payload kind, {hits.Count(hit => hit.InfoHash is not null)} with an info hash"));
        output.WriteLine($"  categories: {string.Join(", ", hits.SelectMany(hit => hit.CategoryIds).Distinct(StringComparer.Ordinal))}");
        output.WriteLine($"  page hosts: {string.Join(", ", Hosts(hits.Select(hit => hit.SourcePageUrl)))}");
        output.WriteLine($"  download hosts: {string.Join(", ", Hosts(hits.Select(hit => hit.Locator.Value)))}");
    }

    private static string Failure(string? error) => error is null ? "" : $": {TorznabLinks.WithoutAddresses(error)}";

    private static IEnumerable<string> Hosts(IEnumerable<string?> links) =>
        links.Select(link => Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : uri?.Scheme ?? "none")
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool HasSession(string? link) => link?.Contains("sid=", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>A movie category, or one in the thousand of a movie parent (2045 under 2000).</summary>
    private static bool IsMovieCategory(string id, IReadOnlySet<string> movieCategories) =>
        movieCategories.Contains(id) || (id.Length == 4 && movieCategories.Contains($"{id[0]}000"));

    private static IntegrationConnectionSettings LiveSettings() =>
        new(
            null,
            Environment.GetEnvironmentVariable(UrlVariable)!.Trim().TrimEnd('/'),
            null,
            null,
            Environment.GetEnvironmentVariable(ApiKeyVariable)!.Trim());
}
