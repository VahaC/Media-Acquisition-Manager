using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementLibraryNotice"/>
/// <remarks>
/// <para>
/// What it says: each name in the quarantine as deleted at its old place, each name put back as created there; a name
/// planned, moving, left where it was, lost or deleted for good is no news to the library. The place is the library
/// folder that holds the old path (<see cref="ReplacementLibraryPlaces"/>, ADR 0003), never the quarantine: Jellyfin
/// skips dot names, so a quarantine path would only make it look at a file it never shows. A video one library holds goes
/// by its copy's path as that library reported it — the spelling the library finds its item by, as MAM-25's read of an
/// old copy does —; otherwise, and for a companion, which no library reported, by each holding library's own path of it.
/// One request per library and change, each path once: two libraries of one Jellyfin are one request.
/// </para>
/// <para>
/// When it is settled — the job's <see cref="ReplacementJobEntity.LibraryNoticeDueUtc"/> cleared, while it still holds
/// the time it was read with —: once every library took it, or when there is nothing to send; at once, logged, when a
/// library can never be told (removed, of a type without the capability); and when a library that can't be told now
/// (switched off, unreachable, failing) still couldn't be once <see cref="ReplacementVerificationRules.LibraryWait"/> from
/// when the notice fell due ran out, with a Warning — Jellyfin's own monitor, or its next scan, catches up on what it
/// wasn't told. Until then it stays owed and each call sends it again, which is harmless.
/// </para>
/// <para>
/// Outside any transaction: it reads, tells the libraries, then makes its one conditional write. Information: the job's
/// id and counts, never a path, a library's name or a sentence.
/// </para>
/// </remarks>
public sealed class ReplacementLibraryNotice(
    ApplicationDbContext db,
    IReplacementLibraryReader library,
    TimeProvider time,
    ILogger<ReplacementLibraryNotice> logger) : IReplacementLibraryNotice
{
    public async Task SendDueAsync(Guid replacementJobId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(replacementJobId, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // The reader never throws on a library's failure; this is a bug or the database failing. It costs this attempt
            // only — the notice stays owed for the next pass — and never the move or the restore that made it due. Error,
            // with the type and the stack, never the message.
            logger.LogError(
                "The library notice of replacement {ReplacementJobId} threw {ExceptionType} {StackTrace}",
                replacementJobId,
                exception.GetType().FullName,
                exception.StackTrace);
        }
    }

    private async Task SendAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        if (await db.ReplacementJobs
                .AsNoTracking()
                .Where(job => job.Id == replacementJobId)
                .Select(job => job.LibraryNoticeDueUtc)
                .SingleOrDefaultAsync(cancellationToken) is not { } dueUtc)
            return;

        var notices = await NoticesAsync(replacementJobId, cancellationToken);
        var waiting = false;
        var givenUp = false;
        foreach (var ((integrationId, change), paths) in notices)
        {
            var answer = await library.TellAsync(integrationId, paths, change, cancellationToken);
            waiting |= answer.Status == ReplacementCheckStatus.Wait;
            givenUp |= answer.Status == ReplacementCheckStatus.Review;
        }

        // Reckoned after the requests, which can take a library's timeout each.
        var now = time.GetUtcNow().UtcDateTime;
        if (waiting && !ReplacementVerificationRules.LibraryWaitPassed(dueUtc, now))
        {
            logger.LogInformation("The library notice of replacement {ReplacementJobId} stays owed: a library couldn't be told now.", replacementJobId);
            return;
        }

        var settled = await db.ReplacementJobs.SettleLibraryNoticeAsync(replacementJobId, dueUtc);
        if (waiting)
            logger.LogWarning(
                "The library notice of replacement {ReplacementJobId} was given up: a library couldn't be told within {Wait} of it falling due.",
                replacementJobId,
                ReplacementVerificationRules.LibraryWait);
        else if (givenUp)
            logger.LogInformation("The library notice of replacement {ReplacementJobId} was given up: a library can never be told.", replacementJobId);
        else
            logger.LogInformation(
                "The library notice of replacement {ReplacementJobId} was taken: {PathCount} paths in {RequestCount} requests; {SettledCount} settled.",
                replacementJobId,
                notices.Sum(notice => notice.Value.Count),
                notices.Count,
                settled);
    }

    /// <summary>
    /// What the job <paramref name="replacementJobId"/>'s names tell each library, by library and change: the paths in
    /// the library's own terms, each once, in the names' order.
    /// </summary>
    private async Task<Dictionary<(Guid IntegrationId, LibraryFileChange Change), IReadOnlyList<string>>> NoticesAsync(
        Guid replacementJobId, CancellationToken cancellationToken)
    {
        var names = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => name.ReplacementJobId == replacementJobId
                && (name.State == ReplacementNameState.Quarantined || name.State == ReplacementNameState.Restored))
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken);
        if (names.Count == 0)
            return [];

        // The copies' paths as their library reported them, which their videos are told by.
        var copyPaths = await db.ReplacementJobFiles
            .AsNoTracking()
            .Where(copy => copy.ReplacementJobId == replacementJobId)
            .ToDictionaryAsync(copy => copy.Id, copy => copy.ServicePath, cancellationToken);
        var folders = (await LibraryFolderReader.LoadAsync(db, cancellationToken)).Mapped;
        var paths = new Dictionary<(Guid IntegrationId, LibraryFileChange Change), List<string>>();
        var unplaced = 0;
        foreach (var name in names)
        {
            var change = name.State == ReplacementNameState.Quarantined ? LibraryFileChange.Deleted : LibraryFileChange.Created;
            var places = NormalizedPath.TryParse(name.AppPath, out var appPath, out _) ? ReplacementLibraryPlaces.Of(appPath, folders) : [];
            if (places.Count == 0)
            {
                // In no library folder the app knows: no library holds it, so none has anything to hear.
                unplaced++;
                continue;
            }

            if (name.Kind == ReplacementNameKind.Video && places is [var only] && copyPaths.TryGetValue(name.ReplacementJobFileId, out var servicePath))
            {
                PathsOf(paths, only.IntegrationId, change).Add(servicePath);
                continue;
            }

            foreach (var place in places)
                PathsOf(paths, place.IntegrationId, change).Add(place.LibraryPath);
        }

        if (unplaced > 0)
            logger.LogInformation(
                "The library notice of replacement {ReplacementJobId} leaves out {UnplacedCount} names no library folder holds.", replacementJobId, unplaced);
        return paths.ToDictionary(notice => notice.Key, notice => (IReadOnlyList<string>)[.. notice.Value.Distinct(StringComparer.Ordinal)]);
    }

    private static List<string> PathsOf(
        Dictionary<(Guid IntegrationId, LibraryFileChange Change), List<string>> paths, Guid integrationId, LibraryFileChange change)
    {
        if (!paths.TryGetValue((integrationId, change), out var list))
            paths[(integrationId, change)] = list = [];
        return list;
    }
}
