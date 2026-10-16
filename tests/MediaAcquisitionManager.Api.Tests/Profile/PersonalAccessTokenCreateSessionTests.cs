using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Profile;

// MAM-5 — creating a token checks the caller's session again inside its own transaction (ADR 0040), as a sign-in and a
// password change do (MAM-4, MAM-16): a reset, disable or delete that commits while a create is under way wins, and no
// token outlives the revocation it raced. The session check refuses every such session before an action runs, so a
// request meets one only in that race, which PersonalAccessTokensHttpTests stages over HTTP; here the service is asked
// the way the controller asks it, with each kind of ended session already in place, and never stores a token for one.
public sealed class PersonalAccessTokenCreateSessionTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string TokenName = "Claude on the laptop";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IUserSeeder _users = null!;
    private AuthorizedUser _member = null!;

    public PersonalAccessTokenCreateSessionTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _users = new UserSeeder(_factory.Scope);
        await _users.CreateOwner("owner");
        _member = await _users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CreateAsync_from_the_users_active_session_creates_the_token_with_its_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();

        //Act
        var result = await tokens.CreateAsync(_member.User.Id, _member.SessionId, Request(), cancellationToken);

        //Assert
        Assert.Equal(PersonalAccessTokenCreateStatus.Created, result.Status);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.PersonalAccessTokens.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((result.Created!.Token.Id, _member.User.Id, (DateTime?)null), (row.Id, row.UserId, row.RevokedUtc));
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((AuditAction.PersonalAccessTokenCreated, (Guid?)row.Id), (audit.Action, audit.TargetId));
        });
    }

    [Theory]
    // The user signed out there, or the owner's reset or disable revoked it.
    [InlineData("revoked")]
    [InlineData("expired")]
    // Disabled while still holding a session the disable hadn't revoked yet.
    [InlineData("disabled")]
    // A session, but someone else's: a token is only ever made from its user's own.
    [InlineData("another user's")]
    // Deleted, taking the session with them: SessionEnded, never a foreign key's exception.
    [InlineData("deleted")]
    public async Task CreateAsync_from_a_session_that_has_ended_is_session_ended_and_stores_nothing(string session)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (userId, sessionId) = await EndedSessionAsync(session, cancellationToken);
        await using var scope = _factory.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>();

        //Act
        var result = await tokens.CreateAsync(userId, sessionId, Request(), cancellationToken);

        //Assert
        Assert.Equal((PersonalAccessTokenCreateStatus.SessionEnded, (CreatedPersonalAccessTokenResponse?)null), (result.Status, result.Created));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.PersonalAccessTokens.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenCreated, cancellationToken));
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static CreatePersonalAccessTokenRequest Request() =>
        new(TokenName, PersonalAccessTokenAccess.ReadAndDownload, PersonalAccessTokenLifetime.Never);

    /// <summary>The user and the session a create would be asked with, the session ended as <paramref name="session"/>
    /// says.</summary>
    private async Task<(Guid UserId, Guid SessionId)> EndedSessionAsync(string session, CancellationToken cancellationToken)
    {
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        switch (session)
        {
            case "revoked":
                await _factory.WithDbAsync(db => db.UserSessions.Where(row => row.Id == _member.SessionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedUtc, (DateTime?)now), cancellationToken));
                return (_member.User.Id, _member.SessionId);
            case "expired":
                await _factory.WithDbAsync(db => db.UserSessions.Where(row => row.Id == _member.SessionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresUtc, now), cancellationToken));
                return (_member.User.Id, _member.SessionId);
            case "disabled":
                var disabled = await _users.CreateAuthorized("disabled", isDisabled: true);
                return (disabled.User.Id, disabled.SessionId);
            case "another user's":
                var other = await _users.CreateAuthorized("other");
                return (_member.User.Id, other.SessionId);
            case "deleted":
                await _factory.WithDbAsync(db => db.Users.Where(user => user.Id == _member.User.Id).ExecuteDeleteAsync(cancellationToken));
                return (_member.User.Id, _member.SessionId);
            default:
                throw new ArgumentOutOfRangeException(nameof(session), session, "Not a way a session ends.");
        }
    }
}
