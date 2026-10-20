using System.Net;
using System.Net.Http.Headers;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

// MAM-24, ADR 0033 — every endpoint of the replacement routes is the installation owner's, its reads too: scheduling a
// replacement is the owner's alone, and the Download dialog's read shows his ticked folders, his files' paths and his
// download clients' torrents. They live in Controllers/Replacements, whose convention (ReplacementsAreOwnerOnlyConvention)
// gates every action there with no attribute on any of them; this sweep — over the running app's endpoints, every method
// each answers, not a hand-kept list — fails the day an endpoint there answers a member, and no replacement, download job
// or audit row written and no request to qBittorrent show the gate came before anything else. MAM-26 — the owner's details,
// Retry, Restore and Delete for good of a replacement live there too, by its id: the sweep sends each with a made-up id and
// no body, and nothing renamed, deleted or made on the disk shows the gate came first there too (the owner's real
// quarantined job is ReplacementRestoreHttpTests.Restore_and_delete_for_good_of_the_owners_quarantined_job_as_a_member_are_403_and_move_nothing).
// MAM-27 — Keep both and Drop join them; and two more sweeps send every discovered route to a real replacement stopped for
// review — each route's parameters filled by name with its id and a real copy's —, so "nothing written" is proved against
// rows a write would change (every replacement row compared before and after), not only against an empty database: the
// owner's, and the member's own. The actions look a job up by its sender, so a member's request on the owner's job would
// write nothing even past a gate that came too late; on his own job Retry, Keep both and Drop would each write, so only
// that sweep shows the gate comes before them. MAM-40 — the owner's removals of a duplicate copy live there too
// (CopyRemovalsController, api/replacements/removals): the sweeps send their {id} routes a real removal resting in the
// quarantine, which Restore and Delete for good would each write to, and every removal row is compared too.
public sealed class ReplacementsAreOwnerOnlyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // Spelled out rather than read from the convention: this pins where the replacement controllers live.
    private const string ReplacementsNamespace = "MediaAcquisitionManager.Api.Controllers.Replacements";
    private const string RemovalsRoute = "api/replacements/removals";
    private static readonly string OwnerOnlyBody = ErrorBody.Json("Only the installation owner can do this.", "account.owner-only");
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.uk.srt";
    private const string ExtendedPath = "/media/movies/Alien (1979)/Alien (1979) Extended.mkv";
    private const string RemovedPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const string AlienImdbId = "tt0078748";
    private const string HeldReason = "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private AuthorizedUser _member = null!;

    public ReplacementsAreOwnerOnlyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        // The owner, and a client and a library that would answer, so a request reaching them would be recorded rather than
        // refused on the way; no replacement, download job or audit row yet.
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Discovery_finds_every_replacement_endpoint_of_today()
    {
        //Arrange

        //Act
        var endpoints = ReplacementEndpoints().Select(endpoint => endpoint.Description).ToHashSet();

        //Assert
        // Guards the guard: a discovery that misses these would let the sweeps below pass for nothing. A superset, so an
        // endpoint added there later passes here and is swept below.
        Assert.Superset(
            new HashSet<string>
            {
                "GET api/replacements/options",
                "POST api/replacements",
                // MAM-26 — the owner's own hand on a replacement.
                "GET api/replacements/{id:guid}",
                "POST api/replacements/{id:guid}/retry",
                "POST api/replacements/{id:guid}/restore",
                "POST api/replacements/{id:guid}/delete-for-good",
                // MAM-27 — Keep both, and Drop of one ticked copy.
                "POST api/replacements/{id:guid}/cancel",
                "POST api/replacements/{id:guid}/copies/{copyId:guid}/drop",
                // MAM-40 — the owner's removals of a duplicate copy, their Restore and Delete for good.
                "GET api/replacements/removals",
                "POST api/replacements/removals",
                "POST api/replacements/removals/{id:guid}/restore",
                "POST api/replacements/removals/{id:guid}/delete-for-good",
            },
            endpoints);
    }

    [Fact]
    public async Task Every_replacement_endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var endpoints = ReplacementEndpoints();

        //Act
        // No body, no query and route values nobody has: the owner check comes before binding and lookups, so neither
        // may turn the answer into a 400, 415 or 404.
        var answers = new List<(string Endpoint, HttpStatusCode Status, string Body)>();
        foreach (var endpoint in endpoints)
        {
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), UrlFor(endpoint.Endpoint.RoutePattern));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _member.AccessToken);
            var response = await _factory.SendHttpRequest(request);
            answers.Add((endpoint.Description, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        //Assert
        Assert.NotEmpty(answers);
        Assert.All(answers, answer => Assert.Equal((HttpStatusCode.Forbidden, OwnerOnlyBody), (answer.Status, answer.Body)));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.ReplacementJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementJobFiles.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementJobNames.CountAsync(cancellationToken));
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            // MAM-40 — no removal either.
            Assert.Equal(0, await db.CopyRemovals.CountAsync(cancellationToken));
            Assert.Equal(0, await db.CopyRemovalNames.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Empty(_factory.Files.DescribedPaths);
        // MAM-26 — nothing renamed, deleted or made through the write contract.
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
    }

    [Fact]
    public async Task Every_replacement_endpoint_as_a_member_on_the_owners_real_stopped_replacement_is_403_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The owner's own replacement: for him Retry, Keep both and Drop would each write to it. MAM-40 — and his removal,
        // which Restore and Delete for good would each write to.
        var planted = await PlantStoppedAsync(requestedByUserId: null);
        var removal = await PlantRemovalAsync();
        var before = await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken);
        var sentBefore = (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count, _factory.Files.DescribedPaths.Count);

        //Act
        var answers = await SweepAsMemberAsync(planted, removal, cancellationToken);

        //Assert
        await AssertRefusedAndUnchangedAsync(planted, removal, answers, before, sentBefore, cancellationToken);
    }

    [Fact]
    public async Task Every_replacement_endpoint_as_a_member_on_his_own_real_stopped_replacement_is_403_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0014 — a job is its sender's, and a member can't send a replacement: this one is planted as a hand-edited row
        // would leave it. The actions find it for him, so Retry, Keep both and Drop would each write to it past a gate that
        // came too late.
        var planted = await PlantStoppedAsync(requestedByUserId: _member.User.Id);
        var removal = await PlantRemovalAsync();
        var before = await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken);
        var sentBefore = (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count, _factory.Files.DescribedPaths.Count);

        //Act
        var answers = await SweepAsMemberAsync(planted, removal, cancellationToken);

        //Assert
        await AssertRefusedAndUnchangedAsync(planted, removal, answers, before, sentBefore, cancellationToken);
    }

    [Fact]
    public async Task Every_replacement_endpoint_without_a_token_is_401()
    {
        //Arrange
        var endpoints = ReplacementEndpoints();

        //Act
        var answers = new List<(string Endpoint, HttpStatusCode Status)>();
        foreach (var endpoint in endpoints)
        {
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), UrlFor(endpoint.Endpoint.RoutePattern));
            answers.Add((endpoint.Description, (await _factory.SendHttpRequest(request)).StatusCode));
        }

        //Assert
        Assert.NotEmpty(answers);
        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Unauthorized, answer.Status));
    }

    /// <summary>
    /// A replacement stopped by the mover, sent by the owner — or by <paramref name="requestedByUserId"/> —, with two ticked
    /// copies: the theatrical cut with names of its move, none of which left its folder, and the extended cut paired with the
    /// new file its check found. Retry, Keep both and Drop would each write to it for its sender.
    /// </summary>
    private Task<PlantedReplacement> PlantStoppedAsync(Guid? requestedByUserId)
    {
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        return new ReplacementQuarantinePlanter(_factory, _harness).PlantAsync(
            ReplacementJobState.NeedsReview,
            [
                new PlantedName(ReplacementNameKind.Video, TheatricalPath, TestMedia.File(inode: 1), ReplacementNameState.Planned),
                new PlantedName(ReplacementNameKind.Companion, SubtitlePath, TestMedia.File(inode: 2, sizeBytes: 98_304), ReplacementNameState.Stayed, PlantedMessage.Of(HeldReason)),
            ],
            quarantineCheckedUtc: now,
            reason: PlantedMessage.Of(HeldReason),
            requestedByUserId: requestedByUserId,
            copies: [new PlantedCopy(ExtendedPath, TestMedia.File(inode: 3), PairedWithNewFile: 0)],
            newFiles:
            [
                new ReplacementNewFileFacts(
                    NewName,
                    ClientSizeBytes: 3_891_719_630,
                    ClientProgress: 1.0,
                    AppPath: NewPath,
                    Disk: TestMedia.File(inode: 7),
                    CheckedUtc: now,
                    Kind: LibraryFileKind.Movie,
                    Refreshed: true,
                    ReadUtc: now,
                    MediaWorkId: Guid.NewGuid(),
                    Identifiers: [(MediaIdentifierScheme.Imdb, AlienImdbId, false)]),
            ],
            inspectionStartedUtc: now,
            libraryAskedUtc: now);
    }

    /// <summary>
    /// MAM-40 — the owner's removal of a copy of Alien, resting in the quarantine: Restore and Delete for good would each write
    /// to it.
    /// </summary>
    private Task<PlantedRemoval> PlantRemovalAsync() =>
        new CopyRemovalPlanter(_factory, _harness).PlantAsync(
            CopyRemovalState.Quarantined, [new PlantedName(ReplacementNameKind.Video, RemovedPath, TestMedia.File(inode: 4))]);

    /// <summary>
    /// Every discovered method of every replacement endpoint, sent as the member to <paramref name="planted"/> — each route
    /// parameter by its name: the job, and a real copy of it; MAM-40 — under <c>api/replacements/removals</c>, the
    /// <paramref name="removal"/> — with no body and no query: the owner check comes before binding and lookups, so neither
    /// may turn the answer into a 400, 415 or 409.
    /// </summary>
    private async Task<List<(string Url, HttpStatusCode Status, string Body)>> SweepAsMemberAsync(
        PlantedReplacement planted, PlantedRemoval removal, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["id"] = planted.Replacement.Id, ["copyId"] = planted.Copies[1].Id };
        var removalRows = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["id"] = removal.Removal.Id };
        var answers = new List<(string Url, HttpStatusCode Status, string Body)>();
        foreach (var endpoint in ReplacementEndpoints())
        {
            var isRemoval = endpoint.Endpoint.RoutePattern.RawText?.StartsWith(RemovalsRoute, StringComparison.Ordinal) == true;
            var url = UrlFor(endpoint.Endpoint.RoutePattern, isRemoval ? removalRows : rows);
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _member.AccessToken);
            var response = await _factory.SendHttpRequest(request);
            answers.Add(($"{endpoint.Method} {url}", response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        return answers;
    }

    /// <summary>
    /// Every answer of a sweep of <paramref name="planted"/> was the owner sentence's 403, and nothing changed: every
    /// replacement row, download job and audit row as in <paramref name="before"/>, no client or library asked and nothing
    /// read on the disk since <paramref name="sentBefore"/>, nothing renamed, deleted or made.
    /// </summary>
    private async Task AssertRefusedAndUnchangedAsync(
        PlantedReplacement planted,
        PlantedRemoval removal,
        IReadOnlyList<(string Url, HttpStatusCode Status, string Body)> answers,
        ReplacementRowsSnapshot before,
        (int QBittorrent, int Jellyfin, int Described) sentBefore,
        CancellationToken cancellationToken)
    {
        // Guards the guard: the routes that name a replacement or a copy were sent the real ones.
        Assert.Contains(answers, answer => answer.Url == $"POST /api/replacements/{planted.Replacement.Id}/cancel");
        Assert.Contains(answers, answer => answer.Url == $"POST /api/replacements/{planted.Replacement.Id}/copies/{planted.Copies[1].Id}/drop");
        Assert.Contains(answers, answer => answer.Url == $"POST /api/replacements/removals/{removal.Removal.Id}/restore");
        Assert.Contains(answers, answer => answer.Url == $"POST /api/replacements/removals/{removal.Removal.Id}/delete-for-good");
        Assert.All(answers, answer => Assert.Equal((HttpStatusCode.Forbidden, OwnerOnlyBody), (answer.Status, answer.Body)));
        Assert.Equal(before, await ReplacementRowsSnapshot.TakeAsync(_factory, cancellationToken));
        Assert.Equal(sentBefore, (_factory.QBittorrentHttp.Requests.Count, _factory.JellyfinHttp.Requests.Count, _factory.Files.DescribedPaths.Count));
        Assert.Empty(_factory.Quarantine.Renames);
        Assert.Empty(_factory.Quarantine.Deletions);
        Assert.Empty(_factory.Quarantine.CreatedFolders);
    }

    // Every endpoint of a replacement controller as the running app routes it, with each method it answers — or POST, for
    // an endpoint that answers every method.
    private List<(RouteEndpoint Endpoint, string Method, string Description)> ReplacementEndpoints() =>
        _factory.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(IsReplacementsEndpoint)
            .SelectMany(endpoint => MethodsOf(endpoint)
                .Select(method => (endpoint, method, $"{method} {endpoint.RoutePattern.RawText}")))
            .ToList();

    private static bool IsReplacementsEndpoint(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(ReplacementsNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(ReplacementsNamespace + ".", StringComparison.Ordinal));

    private static IEnumerable<string> MethodsOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods
            ? methods.Where(method => !HttpMethods.IsHead(method))
            : [HttpMethods.Post];

    // The route with every parameter filled in: a new Guid for a guid, 1 for a number, a word otherwise.
    private static string UrlFor(RoutePattern pattern) => UrlFor(pattern, null);

    // MAM-27 — or each guid by its parameter's name from rows that exist; a guid parameter they don't name throws, so a
    // route added later under a new name can't pass with a made-up id.
    private static string UrlFor(RoutePattern pattern, IReadOnlyDictionary<string, Guid>? rows) =>
        "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => TextOf(part, rows)))));

    private static string TextOf(RoutePatternPart part, IReadOnlyDictionary<string, Guid>? rows) => part switch
    {
        RoutePatternLiteralPart literal => literal.Content,
        RoutePatternSeparatorPart separator => separator.Content,
        RoutePatternParameterPart parameter when HasConstraint(parameter, "guid") => rows is null
            ? Guid.NewGuid().ToString()
            : rows.TryGetValue(parameter.Name, out var row)
                ? row.ToString()
                : throw new InvalidOperationException($"No planted row fills the route parameter {{{parameter.Name}}}."),
        RoutePatternParameterPart parameter when HasConstraint(parameter, "int") || HasConstraint(parameter, "long") => "1",
        RoutePatternParameterPart => "sample",
        _ => throw new InvalidOperationException($"Unexpected route part {part}."),
    };

    private static bool HasConstraint(RoutePatternParameterPart parameter, string name) =>
        parameter.ParameterPolicies.Any(policy => string.Equals(policy.Content, name, StringComparison.OrdinalIgnoreCase));
}
