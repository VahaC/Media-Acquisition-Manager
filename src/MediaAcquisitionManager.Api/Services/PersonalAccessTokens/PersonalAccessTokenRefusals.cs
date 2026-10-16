using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.PersonalAccessTokens;

/// <inheritdoc cref="IPersonalAccessTokenRefusals"/>
public sealed class PersonalAccessTokenRefusals(
    ApplicationDbContext db,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<PersonalAccessTokenRefusals> logger) : IPersonalAccessTokenRefusals
{
    /// <summary>At most one audit row per token in this long: enough to show the owner a script is knocking at a door
    /// it can't open, without one row per knock.</summary>
    private static readonly TimeSpan AuditInterval = TimeSpan.FromMinutes(1);

    public async Task RecordAsync(
        Guid tokenId, Guid userId, PersonalAccessTokenRefusalReason reason, string method, string path, CancellationToken cancellationToken = default)
    {
        // Warning, so it ships off the box: a token refused is either a script someone has to fix or a token in the
        // wrong hands.
        logger.LogWarning(
            "Personal access token {TokenId} of user {UserId} was refused {Method} {Path}: {Reason}.",
            tokenId,
            userId,
            method,
            path,
            reason);

        var now = time.GetUtcNow().UtcDateTime;
        var refusedBefore = now - AuditInterval;
        // MAM-5 — read first, outside any transaction: a script refused in a loop is refused many times a minute, and
        // BEGIN IMMEDIATE (or even an update that matches nothing) would make each of those refusals wait for SQLite's
        // one writer. Only a minute that looks due goes on to the claim below, which settles two refusals at once. A
        // token deleted with its user meanwhile has no row, and gets the log line only.
        var last = await db.PersonalAccessTokens.AsNoTracking()
            .Where(token => token.Id == tokenId)
            .Select(token => new { token.LastRefusedUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (last is null || last.LastRefusedUtc > refusedBefore)
            return;

        // The claim and the row in one transaction: a row is recorded for every minute claimed, and never twice.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await db.PersonalAccessTokens
            .Where(token => token.Id == tokenId && (token.LastRefusedUtc == null || token.LastRefusedUtc <= refusedBefore))
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.LastRefusedUtc, (DateTime?)now), cancellationToken) == 1;
        if (!claimed)
            return;

        var row = await db.PersonalAccessTokens.AsNoTracking()
            .Where(token => token.Id == tokenId)
            .Join(db.Users, token => token.UserId, user => user.Id, (token, user) => new { Token = token, user.UserName })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return;

        auditTrail.Record(AuditAction.PersonalAccessTokenRefused, new AuditActor(row.Token.UserId, row.UserName), AuditTarget.PersonalAccessToken(row.Token));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
