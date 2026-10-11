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
        string? reason = null,
        DateTime? quarantineCheckedUtc = null,
        DateTime? quarantinedUtc = null,
        DateTime? libraryNoticeDueUtc = null,
        DateTime? inspectionStartedUtc = null,
        DateTime? libraryAskedUtc = null)
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
            QuarantineCheckedUtc = quarantineCheckedUtc,
            QuarantinedUtc = quarantinedUtc,
            LibraryNoticeDueUtc = libraryNoticeDueUtc,
            InspectionStartedUtc = inspectionStartedUtc,
            LibraryAskedUtc = libraryAskedUtc,
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
        Guid? mediaWorkId = null,
        string? servicePath = null,
        Guid? pairedNewFileId = null,
        string? problem = null,
        bool onlyDropSettles = false,
        DateTime? droppedUtc = null,
        int? seasonNumber = null,
        int? episodeNumber = null)
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
            ServicePath = servicePath ?? appPath,
            AppPath = appPath,
            DeviceId = facts.DeviceId ?? throw new ArgumentException("A scheduled copy has the identity it was read with.", nameof(facts)),
            Inode = facts.Inode ?? throw new ArgumentException("A scheduled copy has the identity it was read with.", nameof(facts)),
            SizeBytes = facts.SizeBytes,
            ModifiedUtc = facts.ModifiedUtc,
            LinkCount = facts.LinkCount,
            PairedNewFileId = pairedNewFileId,
            Problem = problem,
            OnlyDropSettles = onlyDropSettles,
            DroppedUtc = droppedUtc,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
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

    public Task<ReplacementJobNewFileEntity> AddNewFile(
        Guid replacementJobId,
        int position,
        string relativePath,
        LibraryFileKind? kind = null,
        IReadOnlyList<(MediaIdentifierScheme Scheme, string Value, bool OfSeries)>? identifiers = null) =>
        AddNewFile(replacementJobId, position, new ReplacementNewFileFacts(relativePath, Kind: kind, Identifiers: identifiers));

    public async Task<ReplacementJobNewFileEntity> AddNewFile(Guid replacementJobId, int position, ReplacementNewFileFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var file = new ReplacementJobNewFileEntity
        {
            ReplacementJobId = replacementJobId,
            Position = position,
            RelativePath = facts.RelativePath,
            ClientSizeBytes = facts.ClientSizeBytes,
            ClientProgress = facts.ClientProgress,
            AppPath = facts.AppPath,
            DeviceId = facts.Disk?.DeviceId,
            Inode = facts.Disk?.Inode,
            SizeBytes = facts.Disk?.SizeBytes,
            ModifiedUtc = facts.Disk?.ModifiedUtc,
            DurationMilliseconds = facts.DurationMilliseconds,
            VideoCodec = facts.VideoCodec,
            Width = facts.Width,
            Height = facts.Height,
            CheckedUtc = facts.CheckedUtc,
            LibraryItemId = facts.LibraryItemId,
            Kind = facts.Kind,
            Refreshed = facts.Refreshed,
            SeasonNumber = facts.SeasonNumber,
            EpisodeNumber = facts.EpisodeNumber,
            EpisodeNumberEnd = facts.EpisodeNumberEnd,
            ReadUtc = facts.ReadUtc,
            MediaWorkId = facts.MediaWorkId,
            Problem = facts.Problem,
        };

        db.ReplacementJobNewFiles.Add(file);
        db.ReplacementJobNewFileIdentifiers.AddRange((facts.Identifiers ?? []).Select(identifier => new ReplacementJobNewFileIdentifierEntity
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

    public async Task<ReplacementJobNameEntity> AddName(
        Guid replacementJobId,
        Guid replacementJobFileId,
        int position,
        ReplacementNameKind kind,
        string appPath,
        string quarantinePath,
        string allowedRoot,
        FileFacts facts,
        ReplacementNameState state = ReplacementNameState.Planned,
        string? problem = null,
        DateTime? movedUtc = null,
        DateTime? restoredUtc = null,
        DateTime? deletedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var name = new ReplacementJobNameEntity
        {
            ReplacementJobId = replacementJobId,
            Position = position,
            ReplacementJobFileId = replacementJobFileId,
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

        db.ReplacementJobNames.Add(name);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return name;
    }
}
