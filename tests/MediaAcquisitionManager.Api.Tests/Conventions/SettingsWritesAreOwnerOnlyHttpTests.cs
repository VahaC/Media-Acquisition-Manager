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

// MAM-4 — pins SettingsWritesAreOwnerOnlyConvention: every write that lives under
// Controllers/Settings, found in the running app rather than listed by hand, refuses a signed-in
// non-owner. A settings controller added later is covered the day it appears.
public sealed class SettingsWritesAreOwnerOnlyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // Spelled out rather than read from the convention: this pins where settings controllers live.
    private const string SettingsNamespace = "MediaAcquisitionManager.Api.Controllers.Settings";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;

    public SettingsWritesAreOwnerOnlyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        await users.CreateOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Discovery_finds_every_settings_write_of_today()
    {
        //Arrange

        //Act
        var writes = SettingsWrites().Select(write => write.Description).ToHashSet();

        //Assert
        // Guards the guard: a discovery that misses some or all of these would let the test below
        // pass for less, or for nothing. A superset, so a settings write added later passes here and
        // is swept below.
        Assert.Superset(
            new HashSet<string>
            {
                "POST api/settings/users",
                "POST api/settings/users/{id:guid}/disable",
                "POST api/settings/users/{id:guid}/enable",
                "POST api/settings/users/{id:guid}/password",
                "DELETE api/settings/users/{id:guid}",
                "POST api/settings/integrations",
                "PUT api/settings/integrations/{id:guid}",
                "DELETE api/settings/integrations/{id:guid}",
                "POST api/settings/integrations/{id:guid}/test",
                "POST api/settings/integrations/test",
                "PUT api/settings/allowed-roots",
                "POST api/settings/destinations",
                "PUT api/settings/destinations/{id:guid}",
                "DELETE api/settings/destinations/{id:guid}",
            },
            writes);
    }

    [Fact]
    public async Task Every_settings_write_as_a_signed_in_non_owner_is_403_with_the_owner_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var writes = SettingsWrites();

        //Act
        // No body, and route values nobody has: the owner check comes before binding and lookups,
        // so neither may turn the answer into a 400, 415 or 404.
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
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            // MAM-9 — and no media folder: the allowed roots are a settings write like any other.
            Assert.Equal(0, await db.AllowedRoots.CountAsync(cancellationToken));
            // MAM-11 — and no destination.
            Assert.Equal(0, await db.DestinationMappings.CountAsync(cancellationToken));
        });
    }

    // Every write of a settings controller as the running app routes it: each method an endpoint
    // answers besides GET and HEAD — or POST, for an endpoint that answers every method.
    private List<(RouteEndpoint Endpoint, string Method, string Description)> SettingsWrites() =>
        _factory.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(IsSettingsEndpoint)
            .SelectMany(endpoint => WriteMethodsOf(endpoint)
                .Select(method => (endpoint, method, $"{method} {endpoint.RoutePattern.RawText}")))
            .ToList();

    private static bool IsSettingsEndpoint(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(SettingsNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(SettingsNamespace + ".", StringComparison.Ordinal));

    private static IEnumerable<string> WriteMethodsOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods
            ? methods.Where(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
            : [HttpMethods.Post];

    // The route with every parameter filled in: a new Guid for a guid, 1 for a number, a word
    // otherwise.
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
