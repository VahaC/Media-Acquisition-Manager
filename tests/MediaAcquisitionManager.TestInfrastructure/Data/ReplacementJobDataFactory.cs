using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IReplacementJobDataFactory"/>
public sealed class ReplacementJobDataFactory(ApplicationDbContext db, TimeProvider time) : IReplacementJobDataFactory
{
    public async Task<ReplacementJobEntity> Create(
        Guid downloadJobId,
        Guid requestedByUserId,
        ReplacementJobState state = ReplacementJobState.Scheduled,
        string? reason = null)
    {
        var requestedBy = await db.Users.AsNoTracking().SingleAsync(user => user.Id == requestedByUserId);
        var now = time.GetUtcNow().UtcDateTime;
        var job = new ReplacementJobEntity
        {
            DownloadJobId = downloadJobId,
            RequestedByUserId = requestedBy.Id,
            RequestedByUserName = requestedBy.UserName,
            State = state,
            StateChangedUtc = now,
            Reason = reason,
            CreatedUtc = now,
        };

        db.ReplacementJobs.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return job;
    }

    public async Task<ReplacementJobFileEntity> AddFile(
        Guid replacementJobId,
        string appPath,
        FileFacts facts,
        int position = 0,
        Guid? physicalFileId = null,
        Guid? mediaWorkId = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var workId = mediaWorkId ?? Guid.NewGuid();
        var file = new ReplacementJobFileEntity
        {
            ReplacementJobId = replacementJobId,
            Position = position,
            PhysicalFileId = physicalFileId ?? Guid.NewGuid(),
            MediaVersionId = Guid.NewGuid(),
            MediaWorkId = workId,
            MatchedWorkId = workId,
            ServicePath = appPath,
            AppPath = appPath,
            DeviceId = facts.DeviceId ?? throw new ArgumentException("A scheduled copy has the identity it was read with.", nameof(facts)),
            Inode = facts.Inode ?? throw new ArgumentException("A scheduled copy has the identity it was read with.", nameof(facts)),
            SizeBytes = facts.SizeBytes,
            ModifiedUtc = facts.ModifiedUtc,
            LinkCount = facts.LinkCount,
        };

        db.ReplacementJobFiles.Add(file);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return file;
    }

    public async Task<ReplacementJobWorkEntity> AddWork(
        Guid replacementJobId,
        Guid mediaWorkId,
        MediaWorkKind kind,
        string? title,
        int? year,
        MatchEvidence evidence,
        bool confirmedByOwner,
        string confirmationText,
        IReadOnlyList<(MediaIdentifierScheme Scheme, string Value, bool SetByOwner)>? identifiers = null)
    {
        var work = new ReplacementJobWorkEntity
        {
            ReplacementJobId = replacementJobId,
            MediaWorkId = mediaWorkId,
            Kind = kind,
            Title = title,
            Year = year,
            Evidence = evidence,
            ConfirmedByOwner = confirmedByOwner,
            ConfirmationText = confirmationText,
        };

        db.ReplacementJobWorks.Add(work);
        db.ReplacementJobWorkIdentifiers.AddRange((identifiers ?? []).Select(identifier => new ReplacementJobWorkIdentifierEntity
        {
            ReplacementJobWorkId = work.Id,
            Scheme = identifier.Scheme,
            Value = identifier.Value,
            SetByOwner = identifier.SetByOwner,
        }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return work;
    }

    public async Task<ReplacementJobNewFileEntity> AddNewFile(
        Guid replacementJobId,
        int position,
        string relativePath,
        LibraryFileKind? kind = null,
        IReadOnlyList<(MediaIdentifierScheme Scheme, string Value, bool OfSeries)>? identifiers = null)
    {
        var file = new ReplacementJobNewFileEntity
        {
            ReplacementJobId = replacementJobId,
            Position = position,
            RelativePath = relativePath,
            Kind = kind,
        };

        db.ReplacementJobNewFiles.Add(file);
        db.ReplacementJobNewFileIdentifiers.AddRange((identifiers ?? []).Select(identifier => new ReplacementJobNewFileIdentifierEntity
        {
            ReplacementJobNewFileId = file.Id,
            Scheme = identifier.Scheme,
            Value = identifier.Value,
            OfSeries = identifier.OfSeries,
        }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return file;
    }
}
