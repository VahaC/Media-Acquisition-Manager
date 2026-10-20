using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ICopyRemovalDataFactory"/>
public sealed class CopyRemovalDataFactory(ApplicationDbContext db, TimeProvider time) : ICopyRemovalDataFactory
{
    public async Task<CopyRemovalEntity> Create(
        Guid requestedByUserId,
        CopyRemovalState state,
        string appPath,
        string servicePath,
        string title = "Alien (1979)",
        string groupKey = "0123456789abcdef0123456789abcdef",
        DuplicateClass groupClass = DuplicateClass.SameWork,
        Message? reason = null,
        DateTime? createdUtc = null,
        DateTime? libraryNoticeDueUtc = null)
    {
        var requestedBy = await db.Users.AsNoTracking().SingleAsync(user => user.Id == requestedByUserId);
        var now = createdUtc ?? time.GetUtcNow().UtcDateTime;
        var removal = new CopyRemovalEntity
        {
            RequestedByUserId = requestedBy.Id,
            RequestedByUserName = requestedBy.UserName,
            State = state,
            StateChangedUtc = now,
            Reason = reason,
            Title = title,
            GroupClass = groupClass,
            GroupKey = groupKey,
            PhysicalFileId = Guid.NewGuid(),
            AppPath = appPath,
            ServicePath = servicePath,
            CreatedUtc = now,
            QuarantinedUtc = state == CopyRemovalState.Quarantined ? now : null,
            LibraryNoticeDueUtc = libraryNoticeDueUtc,
        };

        db.CopyRemovals.Add(removal);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return removal;
    }

    public async Task<CopyRemovalNameEntity> AddName(
        Guid copyRemovalId,
        int position,
        ReplacementNameKind kind,
        string appPath,
        string quarantinePath,
        string allowedRoot,
        FileFacts facts,
        ReplacementNameState state = ReplacementNameState.Planned,
        Message? problem = null,
        DateTime? movedUtc = null,
        DateTime? restoredUtc = null,
        DateTime? deletedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var name = new CopyRemovalNameEntity
        {
            CopyRemovalId = copyRemovalId,
            Position = position,
            Kind = kind,
            AppPath = appPath,
            QuarantinePath = quarantinePath,
            AllowedRoot = allowedRoot,
            DeviceId = facts.DeviceId ?? throw new ArgumentException("A planned name has the identity it was read with.", nameof(facts)),
            Inode = facts.Inode ?? throw new ArgumentException("A planned name has the identity it was read with.", nameof(facts)),
            SizeBytes = facts.SizeBytes,
            ModifiedUtc = facts.ModifiedUtc,
            LinkCount = facts.LinkCount,
            State = state,
            Problem = problem,
            MovedUtc = movedUtc,
            RestoredUtc = restoredUtc,
            DeletedUtc = deletedUtc,
        };

        db.CopyRemovalNames.Add(name);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return name;
    }
}
