using System.Net;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// Who may call the Users endpoints at all: a signed-in user who owns the installation — for the
// reads too, as the user list is personal data. Every case runs against every endpoint.
public sealed class UsersAccessHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/users";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private UserEntity _owner = null!;
    private AuthorizedUser _member = null!;

    public UsersAccessHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>Every Users endpoint: its method, and its path below <see cref="BaseUrl"/> with
    /// <c>{id}</c> standing for a user's id.</summary>
    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "" },
        { "GET", "/{id}" },
        { "POST", "" },
        { "POST", "/{id}/disable" },
        { "POST", "/{id}/enable" },
        { "POST", "/{id}/password" },
        { "DELETE", "/{id}" },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateOwner("owner");
        _member = await _users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_without_a_token_is_401_with_the_sign_in_sentence(string method, string path)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await _users.Create("target");

        //Act
        var response = await SendAsync(method, path, target.Id, token: null);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_as_a_signed_in_non_owner_is_403_with_the_owner_sentence_and_changes_nothing(string method, string path)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Disabled where enabling would show, and signed in, so an ended session would show too.
        var target = await _users.CreateAuthorized("target", isDisabled: path.EndsWith("/enable", StringComparison.Ordinal));

        //Act
        var response = await SendAsync(method, path, target.User.Id, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(
                ["MEMBER", "OWNER", "TARGET"],
                await db.Users.Select(u => u.NormalizedUserName).OrderBy(name => name).ToListAsync(cancellationToken));
            var row = await db.Users.SingleAsync(u => u.Id == target.User.Id, cancellationToken);
            Assert.Equal(target.User.IsDisabled, row.IsDisabled);
            Assert.Equal(target.User.PasswordHash, row.PasswordHash);
            Assert.Equal(0, await db.UserSessions.CountAsync(s => s.RevokedUtc != null, cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_as_a_non_owner_with_an_invalid_body_is_403_not_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // The owner check runs before the body is bound or validated, so a non-owner never learns
        // from a 400 what a valid request would look like.
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new CreateUserRequest("x", ""));

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task List_as_a_non_owner_logs_a_warning_naming_the_user()
    {
        //Arrange

        //Act
        await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        var entry = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(InstallationOwnerFilter).FullName);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(_member.User.Id.ToString(), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_as_the_owner_passes_the_gate_without_a_warning()
    {
        //Arrange
        var owner = await _users.CreateSession(_owner);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(InstallationOwnerFilter).FullName);
    }

    // The endpoint's URL with {id} filled in, and the body a real call would send, so the only thing
    // wrong with the request is who sends it.
    private Task<HttpResponseMessage> SendAsync(string method, string path, Guid id, string? token)
    {
        var url = BaseUrl + path.Replace("{id}", id.ToString(), StringComparison.Ordinal);
        return (method, path) switch
        {
            ("GET", _) => _factory.GetHttpRequest(url, token),
            ("POST", "") => _factory.PostHttpRequest(url, token, new CreateUserRequest("newcomer", "newcomer-password")),
            ("POST", "/{id}/password") => _factory.PostHttpRequest(url, token, new ResetPasswordRequest("replacement-password")),
            ("POST", _) => _factory.PostHttpRequest(url, token),
            ("DELETE", _) => _factory.DeleteHttpRequest(url, token),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
    }
}
