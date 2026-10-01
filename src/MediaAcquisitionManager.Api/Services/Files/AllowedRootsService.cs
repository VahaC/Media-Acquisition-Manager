using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <inheritdoc cref="IAllowedRootsService"/>
/// <remarks>
/// MAM-9 — a setting in the database rather than an environment variable, the owner's choice: the
/// folders are edited where the rest of the installation is, and a change takes effect at the next
/// inspection without a restart. A root still has to be a folder the container mounts; the page says so.
/// </remarks>
public sealed class AllowedRootsService(
    ApplicationDbContext db,
    IAuditTrail auditTrail,
    ILogger<AllowedRootsService> logger) : IAllowedRootsService
{
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.AllowedRoots
            .AsNoTracking()
            .OrderBy(root => root.Position)
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);

    public async Task<AllowedRootsResponse> GetAsync(CancellationToken cancellationToken = default) =>
        new(await ListAsync(cancellationToken));

    public async Task<AllowedRootsUpdateResult> UpdateAsync(IReadOnlyList<string?> paths, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (AllowedRootRules.Validate(paths, AllowedRootRules.PlatformStyle) is { } error)
            return AllowedRootsUpdateResult.Invalid(error);

        var roots = AllowedRootRules.Normalize(paths);
        // MAM-4's rule: a save that changes nothing records nothing — the same folders in the same order,
        // however they were typed, write no rows and no audit row claiming a change.
        if ((await ListAsync(cancellationToken)).SequenceEqual(roots, StringComparer.Ordinal))
            return AllowedRootsUpdateResult.Saved;

        var actor = await db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

        // One transaction: the old list goes, the new one and its audit row come, or nothing changes.
        // The delete is the transaction's first write, so a second save waits for this one rather than
        // inserting beside it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AllowedRoots.ExecuteDeleteAsync(cancellationToken);
        db.AllowedRoots.AddRange(roots.Select((path, position) => new AllowedRootEntity { Path = path, Position = position }));
        // No target: the roots belong to the installation, not to a row the audit could name.
        auditTrail.Record(AuditAction.AllowedRootsChanged, actor, target: null);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Information: a count only, never the paths, which say how the host is laid out.
        logger.LogInformation("{ActorUserName} changed the allowed roots; the number allowed is now {Count}.", actor.UserName, roots.Count);
        return AllowedRootsUpdateResult.Saved;
    }
}
