using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.PersonalAccessTokens;

/// <inheritdoc cref="IPersonalAccessTokenService"/>
public sealed class PersonalAccessTokenService(
    ApplicationDbContext db,
    IAuditTrail auditTrail,
    IPersonalAccessTokenMapper mapper,
    TimeProvider time,
    ILogger<PersonalAccessTokenService> logger) : IPersonalAccessTokenService
{
    /// <summary>
    /// The sentence a body that can't be read as a token gets — not JSON, or an access or lifetime that isn't one of the
    /// names — instead of the validation body MVC would send: it names every field and what it takes.
    /// </summary>
    public const string UnreadableBodyError =
        "Send the token as JSON: a name, access ReadOnly or ReadAndDownload, and lifetime Days30, Days90, OneYear or Never.";

    private const string NameRequired = "Give the token a name, such as “Claude on the laptop”.";
    private const string NameTooLong = "A token's name can be at most 100 characters.";
    private const string AccessRequired = "Choose the token's access: Read only or Read and download.";
    private const string LifetimeRequired = "Choose when the token expires.";

    public async Task<PersonalAccessTokenListResponse> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        // Expired ones stay listed, flagged, until revoked: a user whose script stopped sees why. Unpaged (ADR 0040).
        var tokens = await db.PersonalAccessTokens.AsNoTracking()
            .Where(token => token.UserId == userId && token.RevokedUtc == null)
            .OrderByDescending(token => token.CreatedUtc)
            // Two created in one instant still come in one order on every read.
            .ThenBy(token => token.Id)
            .ToListAsync(cancellationToken);
        return new PersonalAccessTokenListResponse([.. tokens.Select(token => mapper.ToDto(token, now))]);
    }

    public async Task<PersonalAccessTokenCreateResult> CreateAsync(
        Guid userId, Guid sessionId, CreatePersonalAccessTokenRequest? request, CancellationToken cancellationToken = default)
    {
        if (Validate(request, out var name) is { } error)
            return PersonalAccessTokenCreateResult.Invalid(error);

        var now = time.GetUtcNow().UtcDateTime;
        var secret = PersonalAccessTokenSecret.Generate();
        var token = new PersonalAccessTokenEntity
        {
            UserId = userId,
            Name = name,
            Prefix = PersonalAccessTokenSecret.DisplayPrefixOf(secret),
            TokenHash = PersonalAccessTokenSecret.HashOf(secret),
            Access = request!.Access!.Value,
            ExpiresUtc = PersonalAccessTokenRules.ExpiresUtc(request.Lifetime!.Value, now),
            CreatedUtc = now,
        };

        // MAM-5 — the session check let this request in before the action ran. An owner's reset, a disable or a delete
        // that committed since must win, as it does over a sign-in (SessionService, MAM-4) and a password change (MAM-16),
        // or this would mint a token the reset's or disable's revocation never saw — one an Enable would bring back. So
        // the session is checked again, as the session check asks it, inside a transaction: EF's BeginTransaction on
        // SQLite is BEGIN IMMEDIATE, which takes the write lock the reset, disable and delete take too, so none of them
        // can commit between this check and the insert. A user deleted meanwhile took their sessions with them, so a
        // create racing a delete ends here too, rather than in a foreign key's 500.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var sessionIsActive = await db.UserSessions.AnyAsync(
            session => session.Id == sessionId
                && session.UserId == userId
                && session.RevokedUtc == null
                && session.ExpiresUtc > now
                && db.Users.Any(user => user.Id == userId && !user.IsDisabled),
            cancellationToken);
        if (!sessionIsActive)
        {
            logger.LogInformation("A personal access token for user {UserId} was not created: the session it was asked from has ended.", userId);
            return PersonalAccessTokenCreateResult.SessionEnded;
        }

        db.PersonalAccessTokens.Add(token);
        var actor = await ActorAsync(userId, cancellationToken);
        auditTrail.Record(AuditAction.PersonalAccessTokenCreated, actor, AuditTarget.PersonalAccessToken(token));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Never the secret, nor its prefix: the log is read by more people than the Profile page.
        logger.LogInformation("{UserName} created personal access token {TokenId} with {Access} access.", actor.UserName, token.Id, token.Access);
        return PersonalAccessTokenCreateResult.Succeeded(new CreatedPersonalAccessTokenResponse(mapper.ToDto(token, now), secret));
    }

    public async Task<PersonalAccessTokenRevokeStatus> RevokeAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default)
    {
        var token = await db.PersonalAccessTokens.AsNoTracking()
            .SingleOrDefaultAsync(token => token.Id == tokenId && token.UserId == userId && token.RevokedUtc == null, cancellationToken);
        if (token is null)
            return PersonalAccessTokenRevokeStatus.NotFound;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Claimed with a conditional update: of two clicks at once, one revokes and records, the other finds it revoked.
        if (!await ClaimRevocationAsync(token.Id, time.GetUtcNow().UtcDateTime, cancellationToken))
            return PersonalAccessTokenRevokeStatus.NotFound;

        var actor = await ActorAsync(userId, cancellationToken);
        auditTrail.Record(AuditAction.PersonalAccessTokenRevoked, actor, AuditTarget.PersonalAccessToken(token));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("{UserName} revoked their personal access token {TokenId}.", actor.UserName, token.Id);
        return PersonalAccessTokenRevokeStatus.Revoked;
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var tokens = await db.PersonalAccessTokens.AsNoTracking()
            .Where(token => token.UserId == userId && token.RevokedUtc == null)
            .OrderBy(token => token.CreatedUtc)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0)
            return 0;

        // The caller's transaction when it has one (a reset or a disable, whose own change commits with this); otherwise
        // one of its own, so no token is revoked without its row.
        var ownTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await using (ownTransaction)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var revoked = 0;
            // One claim per token rather than one UPDATE for all: a row is recorded only for a token this revoked, never
            // for one its user revoked a moment before, and one created meanwhile is left for the next revoke.
            foreach (var token in tokens)
            {
                if (!await ClaimRevocationAsync(token.Id, now, cancellationToken))
                    continue;

                auditTrail.Record(AuditAction.PersonalAccessTokenRevoked, actor, AuditTarget.PersonalAccessToken(token));
                revoked++;
            }

            if (revoked > 0)
                await db.SaveChangesAsync(cancellationToken);
            if (ownTransaction is not null)
                await ownTransaction.CommitAsync(cancellationToken);

            logger.LogInformation("{ActorUserName} revoked {Count} personal access tokens of user {UserId}.", actor.UserName, revoked, userId);
            return revoked;
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The sentence for the first field missing or wrong, in the dialog's order; <c>null</c> with the trimmed
    /// name in <paramref name="name"/>. An access or lifetime sent as a number that names no member counts as
    /// missing.</summary>
    private static string? Validate(CreatePersonalAccessTokenRequest? request, out string name)
    {
        name = request?.Name?.Trim() ?? "";
        if (name.Length == 0)
            return NameRequired;
        if (name.Length > PersonalAccessTokenRules.NameMaximumLength)
            return NameTooLong;
        if (request!.Access is not { } access || !Enum.IsDefined(access))
            return AccessRequired;
        if (request.Lifetime is not { } lifetime || !Enum.IsDefined(lifetime))
            return LifetimeRequired;
        return null;
    }

    private async Task<bool> ClaimRevocationAsync(Guid tokenId, DateTime now, CancellationToken cancellationToken) =>
        await db.PersonalAccessTokens
            .Where(token => token.Id == tokenId && token.RevokedUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedUtc, (DateTime?)now), cancellationToken) == 1;

    // Read from the database rather than the token: the audit row carries the name the account has.
    private Task<AuditActor> ActorAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == userId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);
}
