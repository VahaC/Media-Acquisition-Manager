using System.Net;
using System.Net.Http.Headers;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Http.Probes;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

// MAM-5 — pins the default policy's token check itself (PersonalAccessTokenAccessHandler, ADR 0040), through
// PersonalAccessTokenProbeController: a plain [Authorize] controller as one added tomorrow would be, outside every
// convention. The app's own writes each carry a mark or sit under a convention, so PersonalAccessTokenWritesHttpTests
// would stay green with deny by default inverted for unmarked endpoints; here nothing but the check decides. A session
// is let through everywhere, so each 403 below is the token's.
public sealed class PersonalAccessTokenConventionHttpTests : IClassFixture<PipelineProbeApiFactory>, IAsyncLifetime
{
    // Spelled out rather than read from PersonalAccessTokenErrors: these are what a script's author reads.
    private const string NotForTokensError =
        "An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README.";

    private const string ReadOnlyError =
        "This access token is read only. To send downloads, cancel following or mark Not interested from a script, create a token with “Read and download” access on your Profile page; see “Scripts and the API” in the README.";

    private readonly PipelineProbeApiFactory _factory;
    private AuthorizedUser _member = null!;
    private IssuedPersonalAccessToken _readOnly = null!;
    private IssuedPersonalAccessToken _readAndDownload = null!;

    public PersonalAccessTokenConventionHttpTests(PipelineProbeApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        var tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        await users.CreateOwner("owner");
        _member = await users.CreateAuthorized("member");
        _readOnly = await tokens.Create(_member.User, PersonalAccessTokenAccess.ReadOnly);
        _readAndDownload = await tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Each unmarked write the probe answers, and the action with no method attribute sent as a GET too: it answers every
    // method, so for a token it is a write whatever the request says (fail closed). Refused to either access.
    [Theory]
    [InlineData("POST", PersonalAccessTokenProbeController.BaseUrl, PersonalAccessTokenAccess.ReadOnly)]
    [InlineData("PUT", PersonalAccessTokenProbeController.BaseUrl, PersonalAccessTokenAccess.ReadOnly)]
    [InlineData("DELETE", PersonalAccessTokenProbeController.BaseUrl, PersonalAccessTokenAccess.ReadOnly)]
    [InlineData("GET", PersonalAccessTokenProbeController.AnyMethodUrl, PersonalAccessTokenAccess.ReadOnly)]
    [InlineData("POST", PersonalAccessTokenProbeController.AnyMethodUrl, PersonalAccessTokenAccess.ReadOnly)]
    [InlineData("DELETE", PersonalAccessTokenProbeController.AnyMethodUrl, PersonalAccessTokenAccess.ReadOnly)]
    [InlineData("POST", PersonalAccessTokenProbeController.BaseUrl, PersonalAccessTokenAccess.ReadAndDownload)]
    [InlineData("PUT", PersonalAccessTokenProbeController.BaseUrl, PersonalAccessTokenAccess.ReadAndDownload)]
    [InlineData("DELETE", PersonalAccessTokenProbeController.BaseUrl, PersonalAccessTokenAccess.ReadAndDownload)]
    [InlineData("GET", PersonalAccessTokenProbeController.AnyMethodUrl, PersonalAccessTokenAccess.ReadAndDownload)]
    [InlineData("POST", PersonalAccessTokenProbeController.AnyMethodUrl, PersonalAccessTokenAccess.ReadAndDownload)]
    [InlineData("DELETE", PersonalAccessTokenProbeController.AnyMethodUrl, PersonalAccessTokenAccess.ReadAndDownload)]
    public async Task Unmarked_write_with_a_token_is_403_with_the_sentence(string method, string url, PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = TokenWith(access);

        //Act
        var response = await SendAsync(method, url, token.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotForTokensError, await response.ReadErrorAsync(cancellationToken));
        await AssertOneRefusalOfAsync(token, cancellationToken);
    }

    [Theory]
    [InlineData("POST", PersonalAccessTokenProbeController.BaseUrl)]
    [InlineData("PUT", PersonalAccessTokenProbeController.BaseUrl)]
    [InlineData("DELETE", PersonalAccessTokenProbeController.BaseUrl)]
    [InlineData("GET", PersonalAccessTokenProbeController.AnyMethodUrl)]
    [InlineData("POST", PersonalAccessTokenProbeController.AnyMethodUrl)]
    [InlineData("DELETE", PersonalAccessTokenProbeController.AnyMethodUrl)]
    public async Task Unmarked_write_with_a_session_is_2xx(string method, string url)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await SendAsync(method, url, _member.AccessToken);

        //Assert
        // Guards the guard: the 403s above come from the token check, not from a probe that refuses everyone.
        Assert.True(response.IsSuccessStatusCode, $"{method} {url} answered {(int)response.StatusCode}.");
        await AssertNoRefusalAsync(cancellationToken);
    }

    [Fact]
    public async Task Get_with_a_read_only_token_is_200()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await SendAsync("GET", PersonalAccessTokenProbeController.BaseUrl, _readOnly.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoRefusalAsync(cancellationToken);
    }

    [Theory]
    [InlineData(PersonalAccessTokenAccess.ReadOnly)]
    [InlineData(PersonalAccessTokenAccess.ReadAndDownload)]
    public async Task Post_marked_as_a_read_with_a_token_is_2xx(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await SendAsync("POST", PersonalAccessTokenProbeController.MarkedReadUrl, TokenWith(access).Secret);

        //Assert
        Assert.True(response.IsSuccessStatusCode, $"The marked read answered {(int)response.StatusCode}.");
        await AssertNoRefusalAsync(cancellationToken);
    }

    [Fact]
    public async Task Write_marked_for_read_and_download_with_a_read_only_token_is_403_with_the_read_only_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await SendAsync("POST", PersonalAccessTokenProbeController.MarkedWriteUrl, _readOnly.Secret);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ReadOnlyError, await response.ReadErrorAsync(cancellationToken));
        await AssertOneRefusalOfAsync(_readOnly, cancellationToken);
    }

    [Fact]
    public async Task Write_marked_for_read_and_download_with_a_read_and_download_token_is_2xx()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await SendAsync("POST", PersonalAccessTokenProbeController.MarkedWriteUrl, _readAndDownload.Secret);

        //Assert
        Assert.True(response.IsSuccessStatusCode, $"The marked write answered {(int)response.StatusCode}.");
        await AssertNoRefusalAsync(cancellationToken);
    }

    [Theory]
    [InlineData(PersonalAccessTokenAccess.ReadOnly)]
    [InlineData(PersonalAccessTokenAccess.ReadAndDownload)]
    public async Task Get_marked_as_refused_with_a_token_is_403_with_the_sentence(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = TokenWith(access);

        //Act
        var response = await SendAsync("GET", PersonalAccessTokenProbeController.DeniedUrl, token.Secret);

        //Assert
        // Refused though it reads: the mark wins over the method.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotForTokensError, await response.ReadErrorAsync(cancellationToken));
        await AssertOneRefusalOfAsync(token, cancellationToken);
    }

    [Fact]
    public async Task Get_marked_as_refused_with_a_session_is_200()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await SendAsync("GET", PersonalAccessTokenProbeController.DeniedUrl, _member.AccessToken);

        //Assert
        // The mark is about tokens only: the web app's own session calls the endpoint as before.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoRefusalAsync(cancellationToken);
    }

    private IssuedPersonalAccessToken TokenWith(PersonalAccessTokenAccess access) =>
        access == PersonalAccessTokenAccess.ReadOnly ? _readOnly : _readAndDownload;

    private Task<HttpResponseMessage> SendAsync(string method, string url, string token)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _factory.SendHttpRequest(request);
    }

    // The refusal is recorded against the token that was refused, its user as the actor.
    private Task AssertOneRefusalOfAsync(IssuedPersonalAccessToken token, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.PersonalAccessTokenRefused, audit.Action);
            Assert.Equal(_member.User.Id, audit.ActorUserId);
            Assert.Equal(AuditTargetKind.PersonalAccessToken, audit.TargetKind);
            Assert.Equal(token.Token.Id, audit.TargetId);
        });

    private Task AssertNoRefusalAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
}
