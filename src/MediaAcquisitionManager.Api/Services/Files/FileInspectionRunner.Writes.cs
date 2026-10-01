using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

// What a run writes — a file's outcome, a reading for the cache, the run's progress — and the small
// records it carries them in. Every write takes _writes: the scope's one context serves one operation at
// a time, while the looks and reads run concurrently.
public sealed partial class FileInspectionRunner
{
    /// <summary>
    /// Writes <paramref name="outcome"/> on the file names <paramref name="fileIds"/> and counts them
    /// checked. Set-based, so a name a sync removed meanwhile is simply not there to write.
    /// </summary>
    /// <remarks>
    /// ADR 0009 — an outcome without a reading leaves the name's link to its last reading as it was: a
    /// mount that was away for one run, a root narrowed by mistake or a broken mapping would otherwise
    /// orphan every reading, and step 5 would delete them all, so the next run read the whole disk
    /// again. The status says the facts don't apply now (the page shows them only for
    /// <see cref="FileInspectionStatus.Inspected"/>); when the file is back unchanged, its reading is
    /// found again without a read.
    /// </remarks>
    private async Task WriteAsync(Progress progress, IReadOnlyList<Guid> fileIds, Outcome outcome, CancellationToken cancellationToken)
    {
        var error = outcome.Error is null ? null : FileInspectionRunUpdates.Cut(outcome.Error, MediaInspectionEntity.ErrorMaximumLength);
        FileInspectionStatus? status = outcome.Status;
        var (deviceId, inode, linkCount) = (outcome.Facts?.DeviceId, outcome.Facts?.Inode, outcome.Facts?.LinkCount);
        var readingId = outcome.MediaInspectionId;
        await _writes.WaitAsync(cancellationToken);
        try
        {
            var inspectedUtc = time.GetUtcNow().UtcDateTime;
            await db.PhysicalFiles
                .Where(file => fileIds.Contains(file.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(file => file.InspectionStatus, status)
                        .SetProperty(file => file.InspectionError, error)
                        .SetProperty(file => file.InspectedUtc, inspectedUtc)
                        .SetProperty(file => file.MediaInspectionId, file => readingId ?? file.MediaInspectionId)
                        // The identity of what the name leads to now; unknown again when nothing was
                        // found there, never left as it was (a stale identity would join unrelated
                        // names in Phase B).
                        .SetProperty(file => file.DeviceId, deviceId)
                        .SetProperty(file => file.Inode, inode)
                        .SetProperty(file => file.LinkCount, linkCount),
                    cancellationToken);
            if (readingId is { } linked)
            {
                foreach (var fileId in fileIds)
                {
                    if (_linkedAtStart.TryGetValue(fileId, out var previous) && previous != linked)
                        _replacedReadings.Add(previous);
                }
            }

            progress.AddChecked(fileIds.Count);
            await ReportAsync(progress, cancellationToken);
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <summary>Counts <paramref name="count"/> names checked without writing on them: names of bytes
    /// that changed under the read, left as they were for the next inspection.</summary>
    private async Task SkipAsync(Progress progress, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
            return;

        await _writes.WaitAsync(cancellationToken);
        try
        {
            progress.AddChecked(count);
            await ReportAsync(progress, cancellationToken);
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <summary>
    /// Stores a reading of the bytes <paramref name="key"/> names — the media inspector's facts, or the
    /// sentence saying why it couldn't read them — and returns its id. A reading already stored under the
    /// key is returned instead: the unique index settles it, never a second row.
    /// </summary>
    private async Task<Guid> StoreAsync(CacheKey key, MediaFacts? media, string? error, CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken);
        try
        {
            var reading = new MediaInspectionEntity
            {
                DeviceId = key.DeviceId,
                Inode = key.Inode,
                SizeBytes = key.SizeBytes,
                ModifiedUtc = key.ModifiedUtc,
                Error = error is null ? null : FileInspectionRunUpdates.Cut(error, MediaInspectionEntity.ErrorMaximumLength),
                DurationMilliseconds = media?.Duration is { } duration ? (long)Math.Round(duration.TotalMilliseconds) : null,
                BitRate = media?.BitRate,
                CreatedUtc = time.GetUtcNow().UtcDateTime,
            };
            db.MediaInspections.Add(reading);
            db.MediaInspectionTracks.AddRange((media?.Tracks ?? []).Select((track, position) => new MediaInspectionTrackEntity
            {
                MediaInspectionId = reading.Id,
                Position = position,
                Kind = track.Kind,
                Codec = track.Codec,
                Profile = track.Profile,
                Width = track.Width,
                Height = track.Height,
                DynamicRange = track.DynamicRange,
                Channels = track.Channels,
                ChannelLayout = track.ChannelLayout,
                Language = track.Language,
                IsDefault = track.IsDefault,
                IsForced = track.IsForced,
                IsHearingImpaired = track.IsHearingImpaired,
            }));

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return reading.Id;
            }
            catch (DbUpdateException)
            {
                // Two names that changed into the same bytes between the look and the read, each read
                // under its group: the first reading stands.
                db.ChangeTracker.Clear();
                var existing = await db.MediaInspections
                    .Where(stored => stored.DeviceId == key.DeviceId
                        && stored.Inode == key.Inode
                        && stored.SizeBytes == key.SizeBytes
                        && stored.ModifiedUtc == key.ModifiedUtc)
                    .Select(stored => (Guid?)stored.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                return existing ?? throw new InvalidOperationException("A media inspection could not be stored, and none is stored under its key.");
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        finally
        {
            _writes.Release();
        }
    }

    // The run's progress, absolute rather than added, so writes taken in any order still leave the
    // highest count. Called under _writes.
    private Task ReportAsync(Progress progress, CancellationToken cancellationToken)
    {
        var (checkedCount, probedCount) = (progress.Checked, progress.Probed);
        return db.FileInspectionRuns
            .Where(run => run.Id == progress.RunId && run.Status == FileInspectionRunStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.FilesChecked, checkedCount)
                    .SetProperty(run => run.FilesProbed, probedCount),
                cancellationToken);
    }

    // ── what a run carries ───────────────────────────────────────────────────────────────────────

    /// <summary>A file name, where the app finds it, if anywhere, and the reading it pointed at when loaded.</summary>
    private sealed record FileToInspect(Guid Id, PathMapResult Path, Guid? ReadingId);

    /// <summary>One app path and the names leading to it.</summary>
    private sealed record PathToInspect(string AppPath, List<Guid> FileIds);

    /// <summary>A path and what the file system said about it.</summary>
    private sealed record DescribedPath(PathToInspect Path, FileFacts Facts);

    /// <summary>The cache key of specification §3.3.</summary>
    private readonly record struct CacheKey(long DeviceId, long Inode, long SizeBytes, DateTime ModifiedUtc);

    /// <summary>A stored reading: its id, and its sentence when the inspector couldn't read the bytes.</summary>
    private sealed record CachedReading(Guid Id, string? Error);

    /// <summary>What to write on a file name.</summary>
    private sealed record Outcome(FileInspectionStatus Status, string? Error, Guid? MediaInspectionId, FileFacts? Facts)
    {
        /// <summary>A reading stored for the name's bytes: its facts, or its cached failure.</summary>
        public static Outcome Read(Guid readingId, string? error, FileFacts facts) =>
            new(error is null ? FileInspectionStatus.Inspected : FileInspectionStatus.MediaUnreadable, error, readingId, facts);

        /// <summary>Nothing read, for <paramref name="error"/>'s reason.</summary>
        public static Outcome Problem(FileInspectionStatus status, string? error, FileFacts? facts = null) => new(status, error, null, facts);

        /// <summary>What the inventory said instead of facts.</summary>
        public static Outcome NotRead<T>(FileInventoryResult<T> result) where T : class
        {
            var status = result.Status switch
            {
                FileInventoryStatus.NotFound => FileInspectionStatus.NotFound,
                FileInventoryStatus.OutsideAllowedRoots => FileInspectionStatus.OutsideAllowedRoots,
                FileInventoryStatus.NotARegularFile => FileInspectionStatus.NotARegularFile,
                FileInventoryStatus.MediaUnreadable or FileInventoryStatus.ChangedWhileReading => FileInspectionStatus.MediaUnreadable,
                FileInventoryStatus.InspectorUnavailable => FileInspectionStatus.InspectorUnavailable,
                // Unreadable, and anything an inventory answers without the facts it promised.
                _ => FileInspectionStatus.Unreadable,
            };
            return Problem(status, result.Error ?? "The app couldn't read the file.");
        }
    }

    /// <summary>The run's counts, shared by the concurrent reads.</summary>
    private sealed class Progress(Guid runId)
    {
        private int _checked;
        private int _probed;

        public Guid RunId => runId;

        public int Checked => Volatile.Read(ref _checked);

        public int Probed => Volatile.Read(ref _probed);

        public void AddChecked(int count) => Interlocked.Add(ref _checked, count);

        public void AddProbed() => Interlocked.Increment(ref _probed);
    }
}
