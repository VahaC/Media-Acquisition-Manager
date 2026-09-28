using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// The owner's reads of the user list and the creation of users. Who may call at all is
// UsersAccessHttpTests; disabling, enabling and deleting are UsersLifecycleHttpTests.
public sealed class UsersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/users";
    private const string SignInUrl = "/api/auth/sign-in";
    private const string NewcomerPassword = "newcomer-password";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private AuthorizedUser _owner = null!;

    public UsersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── List ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_is_200_with_every_user_ordered_by_name_whatever_the_letter_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var charlie = await _users.Create("charlie");
        var alice = await _users.Create("Alice");
        var bob = await _users.Create("bob");
        var dave = await _users.Create("Dave", isDisabled: true);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            [
                new UserDto(alice.Id, "Alice", IsOwner: false, IsDisabled: false, alice.CreatedUtc, LastSignInUtc: null),
                new UserDto(bob.Id, "bob", IsOwner: false, IsDisabled: false, bob.CreatedUtc, LastSignInUtc: null),
                new UserDto(charlie.Id, "charlie", IsOwner: false, IsDisabled: false, charlie.CreatedUtc, LastSignInUtc: null),
                new UserDto(dave.Id, "Dave", IsOwner: false, IsDisabled: true, dave.CreatedUtc, LastSignInUtc: null),
                new UserDto(_owner.User.Id, "owner", IsOwner: true, IsDisabled: false, _owner.User.CreatedUtc, LastSignInUtc: null),
            ],
            body!.Items);
        Assert.Equal(5, body.TotalCount);
        // The defaults, as the query named no page.
        Assert.Equal(1, body.Page);
        Assert.Equal(25, body.PageSize);
    }

    [Fact]
    public async Task List_sends_every_instant_with_a_trailing_z()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");
        await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("alice", UserSeeder.DefaultPassword));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Read as text: a DateTime deserializer would accept an instant without its zone.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, item => Assert.EndsWith("Z", item.GetProperty("createdUtc").GetString(), StringComparison.Ordinal));
        var alice = items.Single(item => item.GetProperty("userName").GetString() == "alice");
        Assert.EndsWith("Z", alice.GetProperty("lastSignInUtc").GetString(), StringComparison.Ordinal);
        var owner = items.Single(item => item.GetProperty("userName").GetString() == "owner");
        Assert.Equal(JsonValueKind.Null, owner.GetProperty("lastSignInUtc").ValueKind);
    }

    [Fact]
    public async Task List_pages_on_the_server()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var number = 1; number <= 6; number++)
            await _users.Create($"user-{number}");

        //Act
        // OWNER, USER-1 … USER-6 by name: the second page of three is USER-3 to USER-5.
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=3", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(["user-3", "user-4", "user-5"], body!.Items.Select(user => user.UserName));
        Assert.Equal(7, body.TotalCount);
        Assert.Equal(2, body.Page);
        Assert.Equal(3, body.PageSize);
    }

    [Theory]
    [InlineData(4)]
    // Far enough out that the offset overflows an int: still just an empty page.
    [InlineData(int.MaxValue)]
    public async Task List_past_the_last_page_is_200_with_no_items_and_the_total_count(int page)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var number = 1; number <= 6; number++)
            await _users.Create($"user-{number}");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page={page}&pageSize=3", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Empty(body!.Items);
        Assert.Equal(7, body.TotalCount);
        Assert.Equal(page, body.Page);
    }

    [Theory]
    [InlineData("lic")]
    [InlineData("LIC")]
    [InlineData("Lic")]
    [InlineData(" lic ")]
    public async Task List_searches_on_the_server_for_part_of_the_name_whatever_the_letter_case(string search)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("Alice");
        await _users.Create("malice");
        await _users.Create("bob");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(["Alice", "malice"], body!.Items.Select(user => user.UserName));
        Assert.Equal(2, body.TotalCount);
    }

    [Fact]
    public async Task List_counts_only_the_matches_and_pages_through_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("Alice");
        await _users.Create("malice");
        await _users.Create("bob");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=lic&page=2&pageSize=1", _owner.AccessToken);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(["malice"], body!.Items.Select(user => user.UserName));
        Assert.Equal(2, body.TotalCount);
    }

    [Theory]
    // A LIKE pattern would read '_' as any one character and '%' as anything: every user would match.
    [InlineData("_", "a_b")]
    [InlineData("%", "")]
    public async Task List_search_takes_wildcard_characters_literally(string search, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("a_b");
        await _users.Create("axb");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(expected, string.Join(",", body!.Items.Select(user => user.UserName)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task List_with_a_blank_search_lists_everyone(string search)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        var body = await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(["alice", "owner"], body!.Items.Select(user => user.UserName));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task List_with_a_page_below_1_is_400(int page)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?page={page}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Page must be 1 or more.", await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task List_with_a_page_size_outside_1_to_100_is_400(int pageSize)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?pageSize={pageSize}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Page size must be between 1 and 100.", await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task List_with_a_page_size_at_either_bound_is_200(int pageSize)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("alice");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?pageSize={pageSize}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(pageSize, body!.PageSize);
        Assert.Equal(Math.Min(pageSize, 2), body.Items.Count);
        Assert.Equal(2, body.TotalCount);
    }

    // ── Get ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_is_200_with_the_user()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alice = await _users.Create("alice", isDisabled: true);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{alice.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(new UserDto(alice.Id, "alice", IsOwner: false, IsDisabled: true, alice.CreatedUtc, LastSignInUtc: null), body);
    }

    [Fact]
    public async Task Get_of_the_owner_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{_owner.User.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.True(body!.IsOwner);
    }

    [Fact]
    public async Task Get_of_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // ── Create ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_is_201_with_the_user_and_the_location_that_reads_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest("New.User", NewcomerPassword));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(new UserDto(body!.Id, "New.User", IsOwner: false, IsDisabled: false, now, LastSignInUtc: null), body);
        Assert.Equal($"/api/settings/users/{body.Id}", response.Headers.Location?.AbsolutePath);
        var read = await _factory.GetHttpRequest(response.Headers.Location!.AbsolutePath, _owner.AccessToken);
        Assert.Equal(body, await read.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken));
    }

    [Fact]
    public async Task Create_persists_the_user_trimmed_normalised_and_with_a_hashed_password()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var hasher = _factory.Resolve<IPasswordHasher>();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest("  New.User  ", NewcomerPassword));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id != _owner.User.Id, cancellationToken);
            Assert.Equal("New.User", user.UserName);
            Assert.Equal("NEW.USER", user.NormalizedUserName);
            Assert.NotEqual(NewcomerPassword, user.PasswordHash);
            Assert.DoesNotContain(NewcomerPassword, user.PasswordHash, StringComparison.Ordinal);
            Assert.True(hasher.Verify(NewcomerPassword, user.PasswordHash));
            Assert.False(user.IsDisabled);
            Assert.Equal(now, user.CreatedUtc);
            Assert.Null(user.LastSignInUtc);
        });
    }

    [Fact]
    public async Task Create_records_a_user_created_audit_row_with_the_owner_as_actor_and_the_new_user_as_target()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest("newcomer", NewcomerPassword));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<UserDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.UserCreated, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.User, audit.TargetKind);
            Assert.Equal(body!.Id, audit.TargetId);
            Assert.Equal("newcomer", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Create_lets_the_new_user_sign_in()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest("newcomer", NewcomerPassword));

        //Act
        var response = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("NEWCOMER", NewcomerPassword));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal("newcomer", body!.User.UserName);
        Assert.False(body.User.IsOwner);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("ALICE")]
    [InlineData(" Alice ")]
    public async Task Create_with_a_name_that_exists_in_any_letter_case_is_409_and_writes_nothing(string userName)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("Alice");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest(userName, NewcomerPassword));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"A user named “{userName.Trim()}” already exists.", await response.ReadErrorAsync(cancellationToken));
        await AssertOnlyTheSeededUsersAsync(2, cancellationToken);
    }

    [Fact]
    public async Task Create_twice_at_once_with_one_name_creates_one_user()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // Both may pass the "name is free" check; the unique index on the normalised name must let
        // only one of them commit.
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest("twin", NewcomerPassword)),
            _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest("TWIN", NewcomerPassword)));

        //Assert
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order().ToArray());
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedUserName == "TWIN", cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData("ab", "A user name must be 3 to 32 characters long.")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", "A user name must be 3 to 32 characters long.")]
    [InlineData("two words", "A user name can contain only letters, digits, dots, hyphens and underscores.")]
    [InlineData("new!user", "A user name can contain only letters, digits, dots, hyphens and underscores.")]
    public async Task Create_with_an_invalid_user_name_is_400_with_the_rule_and_writes_nothing(string userName, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest(userName, NewcomerPassword));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertOnlyTheSeededUsersAsync(1, cancellationToken);
    }

    [Theory]
    [InlineData(7, "A password must be at least 8 characters long.")]
    [InlineData(129, "A password can be at most 128 characters long.")]
    public async Task Create_with_a_password_of_the_wrong_length_is_400_with_the_rule_and_writes_nothing(int length, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest("newcomer", new string('p', length)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertOnlyTheSeededUsersAsync(1, cancellationToken);
    }

    // MAM-4 — a blank or missing field reaches the rules and gets their sentence, which the Add user
    // dialog shows as is; a framework validation body has no "error" for it to show.
    [Theory]
    [InlineData("empty user name", "A user name must be 3 to 32 characters long.")]
    [InlineData("whitespace-only user name", "A user name must be 3 to 32 characters long.")]
    [InlineData("missing user name", "A user name must be 3 to 32 characters long.")]
    [InlineData("empty password", "A password must be at least 8 characters long.")]
    [InlineData("whitespace-only password", "A password must be at least 8 characters long.")]
    [InlineData("missing password", "A password must be at least 8 characters long.")]
    [InlineData("empty body", "A user name must be 3 to 32 characters long.")]
    public async Task Create_with_a_blank_or_missing_field_is_400_with_the_rule_and_writes_nothing(string blank, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        object body = blank switch
        {
            "empty user name" => new CreateUserRequest("", NewcomerPassword),
            "whitespace-only user name" => new CreateUserRequest("   ", NewcomerPassword),
            "missing user name" => new { password = NewcomerPassword },
            "empty password" => new CreateUserRequest("newcomer", ""),
            "whitespace-only password" => new CreateUserRequest("newcomer", "   "),
            "missing password" => new { userName = "newcomer" },
            "empty body" => new { },
            _ => throw new ArgumentOutOfRangeException(nameof(blank), blank, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertOnlyTheSeededUsersAsync(1, cancellationToken);
    }

    [Fact]
    public async Task Create_with_a_password_of_eight_spaces_is_201_and_the_spaces_sign_in()
    {
        //Arrange
        var spaces = new string(' ', 8);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateUserRequest("newcomer", spaces));
        var signIn = await _factory.PostHttpRequest(SignInUrl, body: new SignInRequest("newcomer", spaces));

        //Assert
        // Every character counts, spaces included, and nothing trims a password: eight spaces pass
        // the length rule like any eight characters.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    private async Task AssertOnlyTheSeededUsersAsync(int seededUsers, CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(seededUsers, await db.Users.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
