using System.Net;
using System.Net.Http.Headers;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

// MAM-8 — every write of the Library's controllers is the installation owner's. They live outside
// Controllers/Settings, so no convention gates them: each write declares [RequireInstallationOwner]
// itself, and this sweep — over the running app's endpoints, not a hand-kept list — fails the day a write
// is added without it. Reads are for every signed-in user and aren't swept. MAM-34 — the TMDb links'
// controllers below, in Controllers/Library/Links, are owner-only by their namespace's convention, reads
// included (R19, LibraryLinksAreOwnerOnlyHttpTests); their writes are swept here too, and none of them may
// leave a link, an audit row or a request to Jellyfin behind. MAM-21 — nor a decision on a possible match.
public sealed class LibraryWritesAreOwnerOnlyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // Spelled out rather than read from a type: this pins where the Library's controllers live.
    private const string LibraryNamespace = "MediaAcquisitionManager.Api.Controllers.Library";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;

    public LibraryWritesAreOwnerOnlyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        await users.CreateOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Discovery_finds_every_library_write_of_today()
    {
        //Arrange

        //Act
        var writes = LibraryWrites().Select(write => write.Description).ToHashSet();

        //Assert
        // Guards the guard: a discovery that misses this would let the sweep below pass for nothing. A
        // superset, so a library write added later passes here and is swept below.
        Assert.Superset(
            new HashSet<string>
            {
                "POST api/library/sources/{integrationId:guid}/syncs",
                // MAM-9 — the owner's Inspect files.
                "POST api/library/inspections",
                // MAM-18 — the owner's Scan folders.
                "POST api/library/scans",
                // MAM-34 — a work's TMDb link set, removed and written again, and one link written again or removed.
                "PUT api/library/works/{id:guid}/tmdb",
                "DELETE api/library/works/{id:guid}/tmdb",
                "POST api/library/works/{id:guid}/tmdb/write",
                "POST api/library/tmdb/links/{linkId:guid}/write",
                "DELETE api/library/tmdb/links/{linkId:guid}",
                // MAM-34 follow-up — the catch-up of the links set under MAM-34's first rules.
                "POST api/library/tmdb/catch-up",
                // MAM-21 — the owner's Confirm or Dismiss of a possible match, and its Undo.
                "PUT api/library/duplicates/{key}/decision",
                "DELETE api/library/duplicates/{key}/decision",
            },
            writes);
    }

    [Fact]
    public async Task Every_library_write_as_a_signed_in_non_owner_is_403_with_the_owner_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-9 — a folder allowed, so a write that slipped past the owner check would queue an inspection
        // (or, MAM-18, a scan) rather than stop at "no media folders" and write nothing anyway.
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        var writes = LibraryWrites();

        //Act
        // No body, and route values nobody has: the owner check comes before binding and lookups, so
        // neither may turn the answer into a 400, 415 or 404.
        var answers = new List<(string Write, HttpStatusCode Status, string Body)>();
        foreach (var write in writes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(write.Method), UrlFor(write.Endpoint.RoutePattern));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _member.AccessToken);
            var response = await _factory.SendHttpRequest(request);
            answers.Add((write.Description, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        //Assert
        Assert.NotEmpty(answers);
        Assert.All(answers, answer =>
        {
            Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
            Assert.Equal(OwnerOnlyBody, answer.Body);
        });
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.LibrarySyncRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.FileInspectionRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.FolderScanRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.LibraryItemLinks.CountAsync(cancellationToken));
            Assert.Equal(0, await db.DuplicateMatchDecisions.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    // Every write of a Library controller as the running app routes it: each method an endpoint answers
    // besides GET and HEAD — or POST, for an endpoint that answers every method.
    private List<(RouteEndpoint Endpoint, string Method, string Description)> LibraryWrites() =>
        _factory.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(IsLibraryEndpoint)
            .SelectMany(endpoint => WriteMethodsOf(endpoint)
                .Select(method => (endpoint, method, $"{method} {endpoint.RoutePattern.RawText}")))
            .ToList();

    private static bool IsLibraryEndpoint(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(LibraryNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(LibraryNamespace + ".", StringComparison.Ordinal));

    private static IEnumerable<string> WriteMethodsOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods
            ? methods.Where(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
            : [HttpMethods.Post];

    // The route with every parameter filled in: a new Guid for a guid, 1 for a number, a word otherwise.
    private static string UrlFor(RoutePattern pattern) =>
        "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(TextOf))));

    private static string TextOf(RoutePatternPart part) => part switch
    {
        RoutePatternLiteralPart literal => literal.Content,
        RoutePatternSeparatorPart separator => separator.Content,
        RoutePatternParameterPart parameter when HasConstraint(parameter, "guid") => Guid.NewGuid().ToString(),
        RoutePatternParameterPart parameter when HasConstraint(parameter, "int") || HasConstraint(parameter, "long") => "1",
        RoutePatternParameterPart => "sample",
        _ => throw new InvalidOperationException($"Unexpected route part {part}."),
    };

    private static bool HasConstraint(RoutePatternParameterPart parameter, string name) =>
        parameter.ParameterPolicies.Any(policy => string.Equals(policy.Content, name, StringComparison.OrdinalIgnoreCase));
}
