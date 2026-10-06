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

// MAM-34, R19 — every endpoint of the TMDb links' controllers is the installation owner's, its reads too: they change
// the owner's library, show why it refused a write and call the owner's catalog. They live in Controllers/Library/Links,
// whose convention (LibraryLinksAreOwnerOnlyConvention) gates every action there with no attribute on any of them; this
// sweep — over the running app's endpoints, every method each answers, not a hand-kept list — fails the day an endpoint
// there answers a member, and the link written to nothing, no audit row, no suggestion run or search and no request to
// Jellyfin or TMDb show the gate came before anything else.
public sealed class LibraryLinksAreOwnerOnlyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // Spelled out rather than read from the convention: this pins where the links' controllers live.
    private const string LinksNamespace = "MediaAcquisitionManager.Api.Controllers.Library.Links";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;

    public LibraryLinksAreOwnerOnlyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        await users.CreateOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Discovery_finds_every_tmdb_link_endpoint_of_today()
    {
        //Arrange

        //Act
        var endpoints = LinkEndpoints().Select(endpoint => endpoint.Description).ToHashSet();

        //Assert
        // Guards the guard: a discovery that misses these would let the sweeps below pass for nothing. A superset, so an
        // endpoint added there later passes here and is swept below.
        Assert.Superset(
            new HashSet<string>
            {
                "GET api/library/works/{id:guid}/tmdb",
                "PUT api/library/works/{id:guid}/tmdb",
                "DELETE api/library/works/{id:guid}/tmdb",
                "POST api/library/works/{id:guid}/tmdb/write",
                "GET api/library/tmdb/links",
                "POST api/library/tmdb/links/{linkId:guid}/write",
                "DELETE api/library/tmdb/links/{linkId:guid}",
                "GET api/library/tmdb/batches/{batchId:guid}",
                "GET api/library/works/{id:guid}/tmdb/search",
                "GET api/library/works/{id:guid}/tmdb/lookup",
                "PUT api/library/works/{id:guid}/tmdb/none-fits",
                "DELETE api/library/works/{id:guid}/tmdb/none-fits",
                "GET api/library/tmdb/queue",
                "GET api/library/tmdb/confident",
                "POST api/library/tmdb/confident",
                "GET api/library/tmdb/runs/latest",
                "POST api/library/tmdb/runs",
            },
            endpoints);
    }

    [Fact]
    public async Task Every_tmdb_link_endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var endpoints = LinkEndpoints();

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
            Assert.Equal(0, await db.LibraryItemLinks.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            Assert.Equal(0, await db.LibraryLinkSuggestionRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.LibraryLinkSearches.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.JellyfinHttp.Requests);
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task Every_tmdb_link_endpoint_without_a_token_is_401()
    {
        //Arrange
        var endpoints = LinkEndpoints();

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

    // Every endpoint of a links controller as the running app routes it, with each method it answers — or POST, for an
    // endpoint that answers every method.
    private List<(RouteEndpoint Endpoint, string Method, string Description)> LinkEndpoints() =>
        _factory.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(IsLinksEndpoint)
            .SelectMany(endpoint => MethodsOf(endpoint)
                .Select(method => (endpoint, method, $"{method} {endpoint.RoutePattern.RawText}")))
            .ToList();

    private static bool IsLinksEndpoint(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(LinksNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(LinksNamespace + ".", StringComparison.Ordinal));

    private static IEnumerable<string> MethodsOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods
            ? methods.Where(method => !HttpMethods.IsHead(method))
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
