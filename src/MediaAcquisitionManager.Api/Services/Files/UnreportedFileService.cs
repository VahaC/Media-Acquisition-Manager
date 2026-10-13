using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <inheritdoc cref="IUnreportedFileService"/>
/// <remarks>
/// The files are the Media folders row's (<see cref="UnreportedFileReader"/>), and a file counts as "couldn't be read"
/// by the row's own rule (<see cref="FileInspectionCounts.IsProblem"/>), so a tab's count is always the row's.
/// </remarks>
public sealed class UnreportedFileService(
    ApplicationDbContext db,
    ILibraryMapper mapper,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<UnreportedFileService> logger) : IUnreportedFileService
{
    public async Task<UnreportedFileListResult> ListAsync(
        string? view, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return UnreportedFileListResult.Invalid(pagingError);
        if (UnreportedFileListRules.ValidateView(view, out var filter) is { } viewError)
            return UnreportedFileListResult.Invalid(viewError);

        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        // Every file in no library at once and paged here: whether a library reports a name is worked out through the
        // current mappings (LibraryAppPaths), which SQL can't do — the Media folders row's counts read the same set.
        // MAM-44 — an extra is neither counted nor a problem: it has its own tab.
        var files = await UnreportedFileReader.LoadSetAsync(db, cancellationToken);
        var problems = files.Counted.Where(file => FileInspectionCounts.IsProblem(file.InspectionStatus, UnreportedFileReader.HasAppPathOf(file))).ToList();
        var listed = filter switch
        {
            UnreportedFileView.Problems => problems,
            UnreportedFileView.Extras => files.Extras,
            UnreportedFileView.Ignored => files.Ignored,
            _ => files.Counted,
        };
        // In long arithmetic: page × size of a page far past the end would overflow an int.
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var onPage = listed.Skip(skip).Take(pageSize).ToList();

        return UnreportedFileListResult.Listed(new UnreportedFileListResponse(
            await MapAsync(onPage, files, forOwner, cancellationToken),
            listed.Count,
            page,
            pageSize,
            files.Counted.Count,
            problems.Count,
            files.Ignored.Count,
            files.Extras.Count));
    }

    public Task<UnreportedFileIgnoreStatus> IgnoreAsync(Guid fileId, Guid ownerUserId, CancellationToken cancellationToken = default) =>
        SetIgnoredAsync(fileId, ownerUserId, ignore: true, cancellationToken);

    public Task<UnreportedFileIgnoreStatus> UndoIgnoreAsync(Guid fileId, Guid ownerUserId, CancellationToken cancellationToken = default) =>
        SetIgnoredAsync(fileId, ownerUserId, ignore: false, cancellationToken);

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets or clears the file's ignored mark with its audit row, in one transaction; nothing at all when it already
    /// says so, so a double click or a retry records nothing twice.
    /// </summary>
    private async Task<UnreportedFileIgnoreStatus> SetIgnoredAsync(Guid fileId, Guid ownerUserId, bool ignore, CancellationToken cancellationToken)
    {
        // Only a name the scan stored: a library's name is never ignored, and its id is the same bare 404 as none.
        var file = await ScanNames().AsNoTracking().SingleOrDefaultAsync(name => name.Id == fileId, cancellationToken);
        if (file is null)
            return UnreportedFileIgnoreStatus.NotFound;
        if ((file.IgnoredUtc is not null) == ignore)
            return UnreportedFileIgnoreStatus.Done;

        var actor = await db.Users
            .Where(user => user.Id == ownerUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);
        DateTime? ignoredUtc = ignore ? time.GetUtcNow().UtcDateTime : null;
        // Never cancelled once started: the mark and its audit row go together or not at all (the duplicate decision's
        // undo rule).
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        // Guarded by the mark it changes from: a scan may have forgotten the name meanwhile, or another request set the
        // mark first — either way nothing is written, and disposing the transaction rolls nothing back.
        var target = ScanNames().Where(name => name.Id == fileId);
        target = ignore ? target.Where(name => name.IgnoredUtc == null) : target.Where(name => name.IgnoredUtc != null);
        var changed = await target
            .ExecuteUpdateAsync(setters => setters.SetProperty(name => name.IgnoredUtc, ignoredUtc), CancellationToken.None);
        if (changed == 0)
        {
            return await ScanNames().AnyAsync(name => name.Id == fileId, CancellationToken.None)
                ? UnreportedFileIgnoreStatus.Done
                : UnreportedFileIgnoreStatus.NotFound;
        }

        auditTrail.Record(ignore ? AuditAction.UnreportedFileIgnored : AuditAction.UnreportedFileIgnoreUndone, actor, AuditTarget.UnreportedFile(file));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        logger.LogInformation(
            ignore ? "File in no library {FileId} ignored by user {UserId}." : "File in no library {FileId} counted again by user {UserId}.",
            fileId,
            actor.UserId);
        return UnreportedFileIgnoreStatus.Done;
    }

    // The names the scans stored (ADR 0021): the app's own source kind, and no version.
    private IQueryable<PhysicalFileEntity> ScanNames() =>
        db.PhysicalFiles.Where(name => name.SourceKind == ProvenanceSourceKind.FileInspection && name.MediaVersionId == null);

    /// <summary>
    /// The page's files as the list shows them: each with its place among the libraries' folders (<paramref name="set"/>)
    /// and the full facts of its reading — two queries for the readings and tracks of the page.
    /// </summary>
    private async Task<List<UnreportedFileDto>> MapAsync(
        List<PhysicalFileEntity> files, UnreportedFileSet set, bool forOwner, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
            return [];

        var readingIds = files.Select(file => file.MediaInspectionId).OfType<Guid>().Distinct().ToList();
        var readings = await db.MediaInspections.AsNoTracking()
            .Where(reading => EF.Parameter(readingIds).Contains(reading.Id))
            .ToDictionaryAsync(reading => reading.Id, cancellationToken);
        var tracks = (await db.MediaInspectionTracks.AsNoTracking()
                .Where(track => EF.Parameter(readingIds).Contains(track.MediaInspectionId))
                .ToListAsync(cancellationToken))
            .ToLookup(track => track.MediaInspectionId);

        return
        [
            .. files.Select(file => mapper.ToDto(
                file,
                set.PlaceOf(file),
                file.MediaInspectionId is { } readingId ? readings.GetValueOrDefault(readingId) : null,
                file.MediaInspectionId is { } trackReadingId ? [.. tracks[trackReadingId]] : [],
                forOwner)),
        ];
    }
}
