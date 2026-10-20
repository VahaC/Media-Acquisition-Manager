using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// The library's stage: every pass once the library was asked to look — the new files still the ones checked, the library
// read, and the pairing.
public sealed partial class ReplacementVerifier
{
    /// <summary>
    /// Reads what <paramref name="job"/>'s library holds now at its new files and its episode copies, keeps it, and runs
    /// the pairing: Verified, the owner's review, or not yet — until <see cref="ReplacementVerificationRules.LibraryWait"/>
    /// from the request to look ran out. The new files must still be the files the files' checks read.
    /// </summary>
    private async Task CheckLibraryAsync(VerifyingJob job, DateTime now, CancellationToken cancellationToken)
    {
        var replacementJobId = job.Replacement.Id;
        var askedUtc = job.Replacement.LibraryAskedUtc!.Value;
        var waitPassed = ReplacementVerificationRules.LibraryWaitPassed(askedUtc, now);
        var rows = await db.ReplacementJobNewFiles
            .AsTracking()
            .Where(file => file.ReplacementJobId == replacementJobId)
            .OrderBy(file => file.Position)
            .ToListAsync(cancellationToken);
        var rowIds = rows.Select(row => row.Id).ToList();
        var heldIds = await db.ReplacementJobNewFileIdentifiers
            .AsTracking()
            .Where(identifier => rowIds.Contains(identifier.ReplacementJobNewFileId))
            .ToListAsync(cancellationToken);
        // MAM-27 — a copy the owner dropped is out of the job: never read in the library, never paired, never waited on. Its
        // last problem and pair stay as its history. Every new file still goes into the pairing, a dropped copy's own
        // included: one the library hasn't listed, read, identified or numbered yet still holds the job as MAM-25's rule
        // has it — it may be a second file of a copy left —, so the job is verified once every copy left in it is paired
        // and every new file is settled. The works stay whole: a new file identified as a dropped copy's work is then that
        // work's, and simply added, not another work's.
        var copies = await db.ReplacementJobFiles
            .AsTracking()
            .Where(copy => copy.ReplacementJobId == replacementJobId && copy.DroppedUtc == null)
            .OrderBy(copy => copy.Position)
            .ToListAsync(cancellationToken);
        var works = await WorksAsync(replacementJobId, cancellationToken);

        // ── the new files are still the files checked ──
        var roots = await db.AllowedRoots.AsNoTracking().OrderBy(root => root.Position).Select(root => root.Path).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            if (await ReplacementNewFileCheck.ChangedAsync(files, row, roots, cancellationToken) is { } changed)
            {
                ForgetWaitingPass(rows, copies);
                row.Problem = changed;
                await FinishAsync(job, ReplacementJobState.NeedsReview, changed, now, () => Task.CompletedTask);
                return;
            }
        }

        // ── the library: the new files, and the episode copies' own items for their kind, series and numbers ──
        var shows = works.Where(work => work.Kind == MediaWorkKind.Series).Select(work => work.WorkId).ToHashSet();
        var episodeCopies = copies.Where(copy => shows.Contains(copy.MatchedWorkId)).ToList();
        var asks = new Dictionary<Guid, List<string>>();
        foreach (var row in rows)
            PathsOf(asks, row.LibraryIntegrationId!.Value).Add(row.LibraryPath!);
        var folders = episodeCopies.Count > 0 ? (await LibraryFolderReader.LoadAsync(db, cancellationToken)).Mapped : [];
        var copyLibraries = new Dictionary<Guid, Guid>();
        foreach (var copy in episodeCopies)
        {
            // Read in the library whose folder holds it now, by the path that library reported for it; a copy no one
            // library holds any more reads as not listed, which waits and then stops the job.
            if (NormalizedPath.TryParse(copy.AppPath, out var appPath, out _)
                && ReplacementLibraryPlaces.Of(appPath, folders) is [var place])
            {
                PathsOf(asks, place.IntegrationId).Add(copy.ServicePath);
                copyLibraries[copy.Id] = place.IntegrationId;
            }
        }

        var readings = new Dictionary<(Guid IntegrationId, string Path), LibraryFileReading>();
        string? libraryName = null;
        foreach (var (integrationId, paths) in asks)
        {
            var answer = await library.ReadAsync(integrationId, [.. paths.Distinct(StringComparer.Ordinal)], cancellationToken);
            libraryName ??= answer.LibraryName;
            if (answer.Status == ReplacementCheckStatus.Pass)
            {
                foreach (var reading in answer.Readings)
                    readings[(integrationId, reading.ServicePath)] = reading;
                continue;
            }

            // A library that couldn't be read never confirms a file: nothing new is kept, and the job waits — or stops
            // once the hour ran out.
            if (answer.Status == ReplacementCheckStatus.Wait && !waitPassed)
                return;
            ForgetWaitingPass(rows, copies);
            await FinishAsync(job, ReplacementJobState.NeedsReview, answer.Reason, now, () => Task.CompletedTask);
            return;
        }

        // ── the evidence of this read ──
        var newFileInputs = new List<ReplacementPairingNewFile>();
        foreach (var row in rows)
        {
            var reading = readings.GetValueOrDefault((row.LibraryIntegrationId!.Value, row.LibraryPath!));
            Keep(row, reading, heldIds, now);
            newFileInputs.Add(new ReplacementPairingNewFile(row.Id, row.Position, row.AppPath!, ReadingOf(reading), row.DurationMilliseconds));
        }

        await RecordDurationsAsync(copies, cancellationToken);
        // An episode's own titles are no edition word of its new file's name either; a sync that dropped its rows leaves
        // none, as nothing of the episode's titles was kept when it was scheduled.
        var episodeTitles = await TitlesAsync([.. episodeCopies.Select(copy => copy.MediaWorkId)], cancellationToken);
        var copyInputs = new List<ReplacementPairingOldCopy>();
        foreach (var copy in copies)
        {
            ReplacementLibraryReading? reading = null;
            IReadOnlyList<string> titles = [];
            if (episodeCopies.Contains(copy))
            {
                var read = copyLibraries.TryGetValue(copy.Id, out var integrationId)
                    ? readings.GetValueOrDefault((integrationId, copy.ServicePath))
                    : null;
                copy.EpisodeNumberEnd = read is { Listed: true } ? read.Episode?.EpisodeNumberEnd : copy.EpisodeNumberEnd;
                reading = ReadingOf(read);
                titles = [.. episodeTitles[copy.MediaWorkId]];
            }

            copyInputs.Add(new ReplacementPairingOldCopy(
                copy.Id, copy.Position, copy.MatchedWorkId, copy.SeasonNumber, copy.EpisodeNumber, titles, copy.AppPath, reading, copy.DurationMilliseconds));
        }

        // ── the pairing ──
        var result = ReplacementPairing.Pair(
            works, copyInputs, newFileInputs, EditionsOf(job.Replacement.ReleaseEditions), libraryName ?? "", waitPassed);
        foreach (var outcome in result.NewFiles)
        {
            var row = rows.Single(file => file.Id == outcome.NewFileId);
            row.MediaWorkId = outcome.WorkId;
            row.Problem = outcome.Problem;
        }

        foreach (var outcome in result.OldCopies)
        {
            var copy = copies.Single(file => file.Id == outcome.OldCopyId);
            // A pair is kept once the job is decided: verified, or stopped with the copies that paired before it.
            copy.PairedNewFileId = result.Verdict == ReplacementPairingVerdict.Wait ? null : outcome.PairedNewFileId;
            copy.Problem = outcome.Problem;
            // MAM-27 — beside its problem, whether only a drop settles it: the owner's Retry is withheld while one does.
            copy.OnlyDropSettles = outcome.OnlyDropSettles;
        }

        switch (result.Verdict)
        {
            case ReplacementPairingVerdict.Pass:
                await FinishAsync(job, ReplacementJobState.Verified, null, now, () => Task.CompletedTask);
                break;
            case ReplacementPairingVerdict.Review:
                await FinishAsync(job, ReplacementJobState.NeedsReview, result.Reason, now, () => Task.CompletedTask);
                break;
            default:
                await KeepAsync(replacementJobId, () => Task.CompletedTask);
                break;
        }
    }

    /// <summary>
    /// MAM-27 — what the passes before decided nothing with — each copy left in the job's problem, drop-only mark and pair,
    /// and each new file's problem — forgotten when this pass stops the job before the pairing: that stop's reason is what
    /// stopped it. Every pass before it in the library's stage waited (a pass or a review ends the stage), so what it kept
    /// is no verdict: a problem a waiting pass found would read as one that stopped the job, and its mark would withhold the
    /// owner's Retry of a stop a new check may well settle. The library's word on each file stays as its evidence. Staged on
    /// the tracked rows, saved with the stop or with nothing.
    /// </summary>
    private static void ForgetWaitingPass(IReadOnlyList<ReplacementJobNewFileEntity> rows, IReadOnlyList<ReplacementJobFileEntity> copies)
    {
        foreach (var row in rows)
            row.Problem = null;
        foreach (var copy in copies)
        {
            copy.PairedNewFileId = null;
            copy.Problem = null;
            copy.OnlyDropSettles = false;
        }
    }

    /// <summary>Keeps on <paramref name="row"/> what the library said of its item at <paramref name="now"/>, its ids in place
    /// of the last read's (<paramref name="heldIds"/>): what was read last, never a history.</summary>
    private void Keep(
        ReplacementJobNewFileEntity row, LibraryFileReading? reading, IReadOnlyList<ReplacementJobNewFileIdentifierEntity> heldIds, DateTime now)
    {
        var listed = reading is { Listed: true } ? reading : null;
        row.LibraryItemId = listed?.ItemId;
        row.Kind = listed?.Kind;
        row.Refreshed = listed?.Refreshed ?? false;
        row.SeriesItemId = listed?.Episode?.SeriesItemId;
        row.SeasonNumber = listed?.Episode?.SeasonNumber;
        row.EpisodeNumber = listed?.Episode?.EpisodeNumber;
        row.EpisodeNumberEnd = listed?.Episode?.EpisodeNumberEnd;
        row.ReadUtc = now;

        db.ReplacementJobNewFileIdentifiers.RemoveRange(heldIds.Where(identifier => identifier.ReplacementJobNewFileId == row.Id));
        db.ReplacementJobNewFileIdentifiers.AddRange(
            (listed?.Identifiers ?? []).Select(identifier => IdentifierOf(row, identifier, ofSeries: false))
                .Concat((listed?.SeriesIdentifiers ?? []).Select(identifier => IdentifierOf(row, identifier, ofSeries: true))));
    }

    /// <summary>
    /// Each old copy's measured running time, from the media inspector's cached reading of its identity when it was
    /// scheduled (ADR 0009's key); <c>null</c> when none is kept, which leaves the new file's running time uncompared. One
    /// query for them all.
    /// </summary>
    private async Task RecordDurationsAsync(IReadOnlyList<ReplacementJobFileEntity> copies, CancellationToken cancellationToken)
    {
        var deviceIds = copies.Select(copy => copy.DeviceId).Distinct().ToList();
        var inodes = copies.Select(copy => copy.Inode).Distinct().ToList();
        var inspections = await db.MediaInspections
            .AsNoTracking()
            .Where(inspection => deviceIds.Contains(inspection.DeviceId) && inodes.Contains(inspection.Inode))
            .Select(inspection => new { inspection.DeviceId, inspection.Inode, inspection.SizeBytes, inspection.ModifiedUtc, inspection.DurationMilliseconds })
            .ToListAsync(cancellationToken);
        foreach (var copy in copies)
        {
            copy.DurationMilliseconds = inspections
                .FirstOrDefault(inspection => inspection.DeviceId == copy.DeviceId
                    && inspection.Inode == copy.Inode
                    && inspection.SizeBytes == copy.SizeBytes
                    && inspection.ModifiedUtc == copy.ModifiedUtc)
                ?.DurationMilliseconds;
        }
    }

    /// <summary>
    /// The job's matched works as the pairing compares them: each named as the sentences name it, with every title it
    /// goes by now — none is an edition word of a new file's name, as MAM-37 reads names: "Uncut.Gems.2019.mkv" claims
    /// no Uncut cut of a work the dialog showed as "Необроблені алмази" — and its registry ids when it was scheduled,
    /// each once (the owner's own link and the library's may name one id twice). A work whose title rows a sync has
    /// since dropped keeps the title the dialog showed.
    /// </summary>
    private async Task<List<ReplacementPairingWork>> WorksAsync(Guid replacementJobId, CancellationToken cancellationToken)
    {
        var works = await db.ReplacementJobWorks.AsNoTracking().Where(work => work.ReplacementJobId == replacementJobId).ToListAsync(cancellationToken);
        var workIds = works.Select(work => work.Id).ToList();
        var identifiers = await db.ReplacementJobWorkIdentifiers
            .AsNoTracking()
            .Where(identifier => workIds.Contains(identifier.ReplacementJobWorkId))
            .ToListAsync(cancellationToken);
        var titles = await TitlesAsync([.. works.Select(work => work.MediaWorkId)], cancellationToken);
        return
        [
            .. works.Select(work => new ReplacementPairingWork(
                work.MediaWorkId,
                work.Kind,
                ReplacementPairingWork.NameOf(work.Title, work.Year),
                titles[work.MediaWorkId].Any() ? [.. titles[work.MediaWorkId]] : work.Title is { } title ? [title] : [],
                [
                    .. identifiers
                        .Where(identifier => identifier.ReplacementJobWorkId == work.Id)
                        .Select(identifier => new MediaIdentifier(identifier.Scheme, identifier.Value))
                        .Distinct(),
                ])),
        ];
    }

    /// <summary>The titles each of <paramref name="workIds"/> goes by now, every source's, in one query.</summary>
    private async Task<ILookup<Guid, string>> TitlesAsync(IReadOnlyList<Guid> workIds, CancellationToken cancellationToken) =>
        (await db.MediaWorkTitles
            .AsNoTracking()
            .Where(title => workIds.Contains(title.MediaWorkId))
            .Select(title => new { title.MediaWorkId, title.Text })
            .ToListAsync(cancellationToken))
        .ToLookup(title => title.MediaWorkId, title => title.Text);

    private static List<string> PathsOf(Dictionary<Guid, List<string>> asks, Guid integrationId)
    {
        if (!asks.TryGetValue(integrationId, out var paths))
            asks[integrationId] = paths = [];
        return paths;
    }

    /// <summary>What the pairing reads of a library's word on one path: nothing listed when it holds no item there.</summary>
    private static ReplacementLibraryReading ReadingOf(LibraryFileReading? reading) =>
        reading is { Listed: true }
            ? new ReplacementLibraryReading(
                reading.Kind,
                reading.Refreshed,
                reading.Identifiers,
                reading.Episode?.SeriesItemId,
                reading.SeriesIdentifiers,
                reading.Episode?.SeasonNumber,
                reading.Episode?.EpisodeNumber,
                reading.Episode?.EpisodeNumberEnd)
            : ReplacementLibraryReading.NotListed;

    private static ReplacementJobNewFileIdentifierEntity IdentifierOf(ReplacementJobNewFileEntity row, MediaIdentifier identifier, bool ofSeries) =>
        new() { ReplacementJobNewFileId = row.Id, Scheme = identifier.Scheme, Value = identifier.Value, OfSeries = ofSeries };

    /// <summary>
    /// The edition words the release claimed, as the job keeps them (MAM-37's names, comma-separated); a name this build
    /// doesn't know is left out, so a new file claiming it stops the job rather than passing by a guess.
    /// </summary>
    private static IReadOnlyList<MediaEdition> EditionsOf(string releaseEditions) =>
    [
        .. releaseEditions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => Enum.TryParse<MediaEdition>(name, ignoreCase: false, out var edition) && Enum.IsDefined(edition) ? edition : (MediaEdition?)null)
            .OfType<MediaEdition>(),
    ];
}
