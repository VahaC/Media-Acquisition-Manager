using System.Net;
using System.Net.Http.Headers;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
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
// or audit row written and no request to qBittorrent show the gate came before anything else.
public sealed class ReplacementsAreOwnerOnlyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // Spelled out rather than read from the convention: this pins where the replacement controllers live.
    private const string ReplacementsNamespace = "MediaAcquisitionManager.Api.Controllers.Replacements";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;

    public ReplacementsAreOwnerOnlyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        await users.CreateOwner("owner");
        _member = await users.CreateAuthorized("member");
        // A client that would answer, so a request reaching it would be recorded rather than refused on the way.
        new FakeQBittorrentServer().Arm(_factory.QBittorrentHttp);
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
        Assert.Superset(new HashSet<string> { "GET api/replacements/options", "POST api/replacements" }, endpoints);
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
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Empty(_factory.Files.DescribedPaths);
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
