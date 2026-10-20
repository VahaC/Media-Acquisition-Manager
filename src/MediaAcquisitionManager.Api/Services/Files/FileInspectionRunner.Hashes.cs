using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

// MAM-19, ADR 0022 — the run's last step: the files a byte-identical copy could hide among, hashed one at a
// time through the cache, and the hashes of bytes the app no longer knows of forgotten.
public sealed partial class FileInspectionRunner
{
    /// <summary>
    /// MAM-19 — how many files are read whole for their hash at a time: one (Vasyl's decision 3). A hash reads
    /// a file from its first byte to its last, and two such reads on one spinning disk — the reference
    /// installation's — make its head jump between them, so both take longer than one after the other. Files
    /// on several disks, which could be read side by side, wait their turn instead. A parallel loop all the
    /// same, the reads' own, so a change of mind is this one number.
    /// </summary>
    public const int MaximumConcurrentHashes = 1;

    internal static readonly Message ChangedSinceLookError = Message.Of("files.inspection.changed-since-look");

    /// <summary>
    /// Hashes, one at a time and through the cache, the files a byte-identical copy could hide among
    /// (<see cref="SelectHashCandidatesAsync"/>), writing each outcome on the run as it comes; then forgets the
    /// hashes of <paramref name="hashesAtStart"/> whose bytes neither a name nor a reading stands for any more.
    /// </summary>
    /// <param name="progress">The run's counts.</param>
    /// <param name="roots">The allowed roots the run read when it started.</param>
    /// <param name="files">Every name the run loaded, in path order, with its app path.</param>
    /// <param name="hashesAtStart">The hashes stored when the run started.</param>
    /// <param name="cancellationToken">Stops the step; what it stored stays, for the next run.</param>
    private async Task HashAsync(
        Progress progress,
        IReadOnlyList<string> roots,
        IReadOnlyList<FileToInspect> files,
        IReadOnlyList<Guid> hashesAtStart,
        CancellationToken cancellationToken)
    {
        var candidates = await SelectHashCandidatesAsync(files, cancellationToken);
        progress.StartHashing(candidates.Count);
        await db.FileInspectionRuns
            .Where(run => run.Id == progress.RunId && run.Status == FileInspectionRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.HashCandidates, candidates.Count), cancellationToken);

        var hashed = (await db.FileHashes
                .AsNoTracking()
                .Select(hash => new { hash.DeviceId, hash.Inode, hash.SizeBytes, hash.ModifiedUtc })
                .ToListAsync(cancellationToken))
            .Select(hash => new CacheKey(hash.DeviceId, hash.Inode, hash.SizeBytes, hash.ModifiedUtc))
            .ToHashSet();
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = MaximumConcurrentHashes, CancellationToken = cancellationToken },
            async (candidate, token) =>
            {
                if (hashed.Contains(candidate.Key))
                    await HashCheckedAsync(progress, read: false, problem: null, token);
                else
                    await HashOneAsync(progress, roots, candidate, token);
            });

        // A hash lives as long as the app still knows its bytes: a name carries its key, or a reading of those
        // bytes is kept (step 5 decides that by ADR 0009's rule). So a mount that is away, however many runs,
        // costs no hash of bytes the media inspector has a reading of (a cached failure counts), as it costs no
        // reading: its names keep their readings. A file it has none for — the inspector couldn't start, or the
        // file changed under the read — has only its names, which carry no key while the mount is away, so it
        // is read whole again once the mount is back. A file a sync renamed while this run went finds its hash
        // again at the next run, which that sync queued; and a changed file's old hash goes with its old
        // reading, in the run that found the change. Only the hashes stored before this run
        // are its to forget: one it just stored under the key of bytes that changed after its look has neither
        // a name nor a reading yet, and is the next run's.
        await db.FileHashes
            .Where(hash => hashesAtStart.Contains(hash.Id)
                && !db.PhysicalFiles.Any(file => file.DeviceId == hash.DeviceId
                    && file.Inode == hash.Inode
                    && file.ReadSizeBytes == hash.SizeBytes
                    && file.ReadModifiedUtc == hash.ModifiedUtc)
                && !db.MediaInspections.Any(reading => reading.DeviceId == hash.DeviceId
                    && reading.Inode == hash.Inode
                    && reading.SizeBytes == hash.SizeBytes
                    && reading.ModifiedUtc == hash.ModifiedUtc))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Reads one candidate whole through the file inventory, inside the allowed roots, and keeps its hash under
    /// the key the read came with. A file it couldn't read is counted with its reason; nothing is kept for it.
    /// </summary>
    private async Task HashOneAsync(Progress progress, IReadOnlyList<string> roots, HashCandidate candidate, CancellationToken cancellationToken)
    {
        var result = await inventory.HashAsync(candidate.AppPath, roots, cancellationToken);
        switch (result)
        {
            case { Status: FileInventoryStatus.Found, Value: { File: { DeviceId: { } deviceId, Inode: { } inode } file } hash }:
                var readKey = new CacheKey(deviceId, inode, file.SizeBytes, file.ModifiedUtc);
                await StoreHashAsync(readKey, hash.Sha256, cancellationToken);
                // The file changed between the look and the hash, and stayed put while it was read: the hash is
                // the new bytes', kept under their key, which the names carry only from the next look on. Until
                // then they have no hash, which the owner is told.
                await HashCheckedAsync(progress, read: true, readKey == candidate.Key ? null : ChangedSinceLookError, cancellationToken);
                break;
            case { Status: FileInventoryStatus.Found }:
                // An unknown identity beats a guessed one: nothing is kept under a key that can't tell two files apart.
                await HashCheckedAsync(progress, read: true, IdentityUnknownError, cancellationToken);
                break;
            default:
                // Gone, moved out of the roots, locked, or changed while it was read (the inventory's reason
                // says which): never a hash of bytes that may not be the file's, and never a failure of the run.
                // The next run tries again.
                await HashCheckedAsync(progress, read: false, result.Error ?? UnreadableError, cancellationToken);
                break;
        }
    }

    // ── which files need a hash ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ADR 0022 — the files a byte-identical copy could hide among, each once, in path order: a file whose size
    /// a file of another identity shares, unless their media facts tell the two apart. Two names of one file
    /// are one file, and the same file twice is no copy, so a file whose size only its own names share is
    /// never read. Files of one size whose facts disagree (another duration, other tracks) can't be the same
    /// bytes, and a fact unknown on either side — a file ffprobe couldn't read — tells nothing apart.
    /// </summary>
    /// <remarks>
    /// Taken from the names as the steps above left them: each carries the identity, size and modification time
    /// this run's look read (a name it found nothing at carries none), and the reading the media inspector gave
    /// those bytes. A name a sync added while the run went has no app path here and no identity yet.
    /// </remarks>
    private async Task<List<HashCandidate>> SelectHashCandidatesAsync(IReadOnlyList<FileToInspect> files, CancellationToken cancellationToken)
    {
        var looked = await db.PhysicalFiles
            .AsNoTracking()
            .Where(file => file.DeviceId != null && file.Inode != null && file.ReadSizeBytes != null && file.ReadModifiedUtc != null)
            .Select(file => new { file.Id, file.DeviceId, file.Inode, file.ReadSizeBytes, file.ReadModifiedUtc, file.InspectionStatus, file.MediaInspectionId })
            .ToDictionaryAsync(file => file.Id, cancellationToken);

        // One file per key, named by its first name in path order; its reading counts only while the look found
        // the media inspector's facts current (the page's rule, LibraryMapper).
        var keyed = new Dictionary<CacheKey, HashCandidate>();
        foreach (var file in files)
        {
            if (!file.Path.HasAppPath || !looked.TryGetValue(file.Id, out var name))
                continue;

            var key = new CacheKey(name.DeviceId!.Value, name.Inode!.Value, name.ReadSizeBytes!.Value, name.ReadModifiedUtc!.Value);
            var readingId = name.InspectionStatus == FileInspectionStatus.Inspected ? name.MediaInspectionId : null;
            if (!keyed.TryGetValue(key, out var known))
                keyed[key] = new HashCandidate(key, file.Path.AppPath!, keyed.Count, readingId);
            else if (known.ReadingId is null && readingId is not null)
                keyed[key] = known with { ReadingId = readingId };
        }

        var shared = keyed.Values
            .GroupBy(file => file.Key.SizeBytes)
            .Where(size => size.Select(file => (file.Key.DeviceId, file.Key.Inode)).Distinct().Count() > 1)
            .ToList();
        var facts = await MediaFingerprintsAsync([.. shared.SelectMany(size => size).Select(file => file.ReadingId).OfType<Guid>()], cancellationToken);
        return
        [
            .. shared
                .SelectMany(size => size.Where(file => size.Any(other =>
                    (other.Key.DeviceId, other.Key.Inode) != (file.Key.DeviceId, file.Key.Inode)
                    && CouldBeCopies(FingerprintOf(file, facts), FingerprintOf(other, facts)))))
                .OrderBy(file => file.Order),
        ];
    }

    /// <summary>
    /// The facts of the readings <paramref name="readingIds"/> that a byte-identical copy would share: how long
    /// it plays and, track by track, what kind of track, codec, picture size, channel count and language — the
    /// facts a newer ffprobe is least likely to word differently, so two readings made by different versions
    /// don't set two copies apart. A reading that is a cached failure has none.
    /// </summary>
    private async Task<Dictionary<Guid, MediaFingerprint>> MediaFingerprintsAsync(List<Guid> readingIds, CancellationToken cancellationToken)
    {
        if (readingIds.Count == 0)
            return [];

        var readings = await db.MediaInspections
            .AsNoTracking()
            .Where(reading => readingIds.Contains(reading.Id) && reading.Error == null)
            .Select(reading => new { reading.Id, reading.DurationMilliseconds })
            .ToListAsync(cancellationToken);
        var tracks = (await db.MediaInspectionTracks
                .AsNoTracking()
                .Where(track => readingIds.Contains(track.MediaInspectionId))
                .Select(track => new { track.MediaInspectionId, track.Position, track.Kind, track.Codec, track.Width, track.Height, track.Channels, track.Language })
                .ToListAsync(cancellationToken))
            .ToLookup(track => track.MediaInspectionId);
        return readings.ToDictionary(
            reading => reading.Id,
            reading => new MediaFingerprint(
                reading.DurationMilliseconds,
                [
                    .. tracks[reading.Id]
                        .OrderBy(track => track.Position)
                        .Select(track => new TrackFingerprint(track.Kind, track.Codec, track.Width, track.Height, track.Channels, track.Language)),
                ]));
    }

    private static MediaFingerprint? FingerprintOf(HashCandidate file, Dictionary<Guid, MediaFingerprint> facts) =>
        file.ReadingId is { } readingId ? facts.GetValueOrDefault(readingId) : null;

    /// <summary>
    /// Whether two files' media facts leave room for them to be the same bytes: the same bytes give the same
    /// facts, so any fact both know and disagree on rules it out; one either doesn't know rules out nothing.
    /// </summary>
    private static bool CouldBeCopies(MediaFingerprint? one, MediaFingerprint? other)
    {
        if (one is null || other is null)
            return true;
        if (!Agree(one.DurationMilliseconds, other.DurationMilliseconds) || one.Tracks.Count != other.Tracks.Count)
            return false;

        return one.Tracks.Zip(other.Tracks).All(pair =>
            pair.First.Kind == pair.Second.Kind
            && Agree(pair.First.Codec, pair.Second.Codec)
            && Agree(pair.First.Width, pair.Second.Width)
            && Agree(pair.First.Height, pair.Second.Height)
            && Agree(pair.First.Channels, pair.Second.Channels)
            && Agree(pair.First.Language, pair.Second.Language));
    }

    private static bool Agree<T>(T? one, T? other) where T : struct => one is null || other is null || one.Value.Equals(other.Value);

    private static bool Agree(string? one, string? other) => one is null || other is null || string.Equals(one, other, StringComparison.Ordinal);

    // ── what the step carries ───────────────────────────────────────────────────────────────────

    /// <summary>A file that may need a hash: its key, the first of its names in path order, where that comes in
    /// that order, and the media inspector's reading of its bytes when the look found it current.</summary>
    private sealed record HashCandidate(CacheKey Key, string AppPath, int Order, Guid? ReadingId);

    /// <summary>The facts of a reading a byte-identical copy shares; a <c>null</c> value is unknown.</summary>
    private sealed record MediaFingerprint(long? DurationMilliseconds, IReadOnlyList<TrackFingerprint> Tracks);

    /// <summary>The facts of one track a byte-identical copy shares; a <c>null</c> value is unknown.</summary>
    private sealed record TrackFingerprint(MediaTrackKind Kind, string? Codec, int? Width, int? Height, int? Channels, string? Language);
}
