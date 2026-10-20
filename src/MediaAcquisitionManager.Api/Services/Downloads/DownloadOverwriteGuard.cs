using System.Diagnostics;
using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <inheritdoc cref="IDownloadOverwriteGuard"/>
/// <remarks>
/// <para>
/// ADR 0034 — Vasyl's decisions of 2026-10-08. Each place one of the release's files may be written
/// (<see cref="TorrentTargetPaths"/>) is looked at inside the Media folders: nothing there, or the same bytes — the
/// same size, and the torrent's hash of each of up to three pieces wholly inside it — is fine, as a Toloka update
/// re-adding a series' old episodes is; a file of another size or other bytes, something that isn't a file, or one
/// the app can't read refuses the send. A place nothing is at yet is also compared with the torrents the client
/// already has in the destination, which write their files only once they download.
/// </para>
/// <para>
/// What the app can't look at is sent as before and said so on the job: a magnet, which names no files until the
/// client fetches them; a .torrent whose list of files the app can't read; a destination whose folder has no path
/// in the app or lies outside the Media folders, which the Download dialog warns of before Send; a place behind a
/// link that leads out of them, or whose name the app can't look up; and a client that can't list its torrents or
/// their files. A place that can't be looked at never hides one that can: the loop always runs to its end, and any
/// collision refuses the send. A refusal is the app's own sentence, read by everyone who sent the release.
/// </para>
/// <para>
/// MAM-30 — where and under which names a client writes a torrent's files is its type's
/// (<see cref="DownloadClientCapabilities.FilePlacement"/>): Transmission writes only the torrent's own layout, and an
/// unfinished file under its name with <c>.part</c> (<see cref="DownloadClientCapabilities.PartialFileSuffix"/>), a place
/// looked at like any other. A folder destination's torrents are every torrent the client saves in that folder, and its
/// refusals name the destination, never the folder, as members read them (D13). A folder the client can't see has no
/// path in the app: it is never looked into, and the check refuses a send there in either mode, as the send does
/// before it (D5).
/// </para>
/// <para>
/// MAM-24, ADR 0032 — a replacement's check is strict (<c>anyFileRefuses</c>): a file already at a place refuses it
/// whatever its bytes, and each of the ways above the check couldn't look refuses it instead of sending it unchecked.
/// Its scope is MAM-38's (R7): a place nothing is at yet is compared with the torrents of the destination's own
/// category, and a torrent of another category writing there later is MAM-25's to catch.
/// </para>
/// </remarks>
public sealed class DownloadOverwriteGuard(ApplicationDbContext db, IFileInventory files, ILogger<DownloadOverwriteGuard> logger) : IDownloadOverwriteGuard
{
    /// <summary>
    /// The torrents whose files are asked for, at most, when a place nothing is at yet may be one they write: a
    /// category holds hundreds of torrents, and only one with a folder or file of the same name — or one still in
    /// the folder for incomplete torrents — can write there.
    /// </summary>
    private const int MaximumJobsAskedForFiles = 20;

    /// <inheritdoc/>
    public async Task<DownloadOverwriteCheck?> FolderReasonAsync(CheckedClientDestination? destination, CancellationToken cancellationToken = default) =>
        (await FolderAsync(destination, cancellationToken)).Reason;

    /// <inheritdoc/>
    public async Task<DownloadOverwriteVerdict> CheckAsync(
        ReleasePayload payload,
        CheckedClientDestination destination,
        string destinationName,
        IntegrationConnectionEntity client,
        IDownloadClient downloadClient,
        IntegrationConnectionSettings settings,
        bool anyFileRefuses = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(client);

        // MAM-30 — a folder its client can't see takes nothing, Keep both or a replacement alike, whatever the release is:
        // the client would create it in its own filesystem (D5). The send refuses it before asking, and so does the check
        // itself, never "sent unchecked", with the send's own sentence, which names the destination (D13).
        if (destination.Check.Status == DestinationPathStatus.FolderNotSeen)
            return DownloadOverwriteVerdict.Refuse(DownloadSender.FolderUnusableError(client, destinationName));

        var wording = new DestinationWording(downloadClient.Capabilities.DestinationKind, destination.Destination.Key, destinationName);
        // MAM-24 — what the check couldn't look at: sent and said so, or, strict, refused with why.
        DownloadOverwriteVerdict Unchecked(DownloadOverwriteCheck check) =>
            anyFileRefuses
                ? DownloadOverwriteVerdict.Refuse(UncheckedRefusalOf(wording, check))
                : DownloadOverwriteVerdict.Send(check);

        if (payload.Kind == ReleasePayloadKind.Magnet)
            return Unchecked(DownloadOverwriteCheck.NotCheckedMagnet);
        if (payload.TorrentFile is not { } torrent || TorrentContentReader.Read(torrent) is not { } content)
            return Unchecked(DownloadOverwriteCheck.NotCheckedFileList);

        var (folder, roots, reason) = await FolderAsync(destination, cancellationToken);
        if (reason is { } notChecked)
            return Unchecked(notChecked);
        // MAM-30 — a file no reading of its names places, where the client writes can't be told.
        if (TorrentTargetPaths.For(folder!, content, downloadClient.Capabilities.FilePlacement, downloadClient.Capabilities.PartialFileSuffix) is not { } targets)
            return Unchecked(DownloadOverwriteCheck.NotCheckedFileList);

        var collisions = new List<Collision>();
        var vacant = new List<TorrentTarget>();
        // Remembered, never answered from inside the loop: a place that can't be looked at must not send a release
        // whose other places would refuse it.
        var behindLinkOut = false;
        var nameNotLookedUp = false;
        foreach (var target in targets)
        {
            var path = target.Path.ToString();
            // A name libtorrent keeps — DEL, a C1 control — that the app's paths refuse: the inventory would call
            // it outside the Media folders, so it isn't asked at all.
            if (!NormalizedPath.TryParse(path, out _, out _))
            {
                nameNotLookedUp = true;
                continue;
            }

            var described = await files.DescribeAsync(path, roots, cancellationToken);
            switch (described.Status)
            {
                case FileInventoryStatus.Found when anyFileRefuses:
                    // MAM-24 — a replacement never lands on a file already there, the same bytes included: being there
                    // refuses it, and its pieces are never read.
                    collisions.Add(new Collision(target, CollisionReason.AlreadyThere, null));
                    break;
                case FileInventoryStatus.Found:
                    // Once one collision refuses the send, the others are told by their size alone: reading more
                    // pieces changes nothing.
                    if (await CompareAsync(target, described.Value!, roots, compareContent: collisions.Count == 0, cancellationToken) is { } why)
                        collisions.Add(new Collision(target, why, null));
                    break;
                case FileInventoryStatus.NotFound:
                    vacant.Add(target);
                    break;
                case FileInventoryStatus.NotARegularFile:
                    collisions.Add(new Collision(target, CollisionReason.NotAFile, null));
                    break;
                case FileInventoryStatus.OutsideAllowedRoots:
                    // A link on the way that leads out of the Media folders: that place can't be looked into.
                    behindLinkOut = true;
                    break;
                default:
                    collisions.Add(new Collision(target, CollisionReason.Unreadable, null));
                    break;
            }
        }

        if (collisions.Count > 0)
            return DownloadOverwriteVerdict.Refuse(RefusalOf(client, wording, folder!, collisions));

        var queued = vacant.Count > 0
            ? await QueuedCollisionAsync(vacant, destination, client, downloadClient, settings, cancellationToken)
            : (Complete: true, Collision: null);
        if (queued.Collision is { } collision)
            return DownloadOverwriteVerdict.Refuse(RefusalOf(client, wording, folder!, [collision]));

        if (behindLinkOut)
            return Unchecked(DownloadOverwriteCheck.NotCheckedFolderOutsideMediaFolders);
        if (nameNotLookedUp)
            return Unchecked(DownloadOverwriteCheck.NotCheckedFileName);
        if (!queued.Complete)
            return Unchecked(DownloadOverwriteCheck.NotCheckedClientJobs);
        return DownloadOverwriteVerdict.Send(DownloadOverwriteCheck.Checked);
    }

    /// <summary>
    /// The destination's folder inside the app and the Media folders, or why it can't be looked into: no path the
    /// app can use, or none of the Media folders holds it, by its text — the inventory follows every link itself.
    /// MAM-30 — a folder its client can't see is never looked into, whatever its mappings say (D5), and has a reason of
    /// its own: nothing is sent there at all, which the Download dialog says rather than "sent unchecked".
    /// </summary>
    private async Task<(NormalizedPath? Folder, IReadOnlyList<string> Roots, DownloadOverwriteCheck? Reason)> FolderAsync(
        CheckedClientDestination? destination, CancellationToken cancellationToken)
    {
        if (destination?.Check.Status == DestinationPathStatus.FolderNotSeen)
            return (null, [], DownloadOverwriteCheck.NotCheckedFolderNotSeen);
        if (destination?.Check.AppPath is not { } appPath || !NormalizedPath.TryParse(appPath, out var folder, out _))
            return (null, [], DownloadOverwriteCheck.NotCheckedFolderUnknown);

        // Read for every check, as the inspection reads them: the owner's setting, never one cached elsewhere.
        var roots = await db.AllowedRoots.AsNoTracking()
            .OrderBy(root => root.Position)
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);
        var inside = roots.Any(root => NormalizedPath.TryParse(root, out var parsed, out _) && folder.IsUnder(parsed));
        return inside ? (folder, roots, null) : (null, [], DownloadOverwriteCheck.NotCheckedFolderOutsideMediaFolders);
    }

    /// <summary>
    /// Why the file already at <paramref name="target"/> isn't the torrent's own there; <c>null</c> when it is: the
    /// same size, and each of its sampled pieces hashing as the torrent says. A file with no piece wholly inside it —
    /// a small one between two others in a v1 torrent — is told by its size. Without
    /// <paramref name="compareContent"/> — a collision already refuses the send — any other file of the torrent's
    /// size is counted as already there, unread: its bytes change nothing about the refusal.
    /// </summary>
    private async Task<CollisionReason?> CompareAsync(
        TorrentTarget target, FileFacts facts, IReadOnlyList<string> roots, bool compareContent, CancellationToken cancellationToken)
    {
        if (facts.SizeBytes != target.File.Length)
            return CollisionReason.DifferentSize;
        if (target.File.Samples.Count == 0)
            return null;
        if (!compareContent)
            return CollisionReason.NotCompared;

        var hashed = await files.HashPiecesAsync(
            target.Path.ToString(), roots, [.. target.File.Samples.Select(sample => sample.Range)], cancellationToken);
        if (hashed.Value is not { } pieces)
            return hashed.Status == FileInventoryStatus.ChangedWhileReading ? CollisionReason.Changed : CollisionReason.Unreadable;

        for (var index = 0; index < target.File.Samples.Count; index++)
        {
            if (!pieces.Hashes[index].AsSpan().SequenceEqual(target.File.Samples[index].ExpectedHash))
                return CollisionReason.DifferentContent;
        }

        return null;
    }

    /// <summary>
    /// The first place nothing is at yet that a torrent the client already has in the destination will write — its
    /// one file, or one of its files —, and whether every such torrent could be told: the client listed them, each
    /// has its metadata, and the files asked for came back.
    /// </summary>
    /// <remarks>
    /// A torrent's files end up below its save path, by their paths relative to it, whatever its content path says:
    /// qBittorrent keeps an unfinished download in its folder for incomplete torrents, if one is set, and moves it
    /// once finished. Every torrent is first compared by what its listing alone tells — its content where it is, or
    /// its name in its save path —, and only then are the files of those that may write one of the places asked for,
    /// at most <see cref="MaximumJobsAskedForFiles"/> of them.
    /// </remarks>
    private async Task<(bool Complete, Collision? Collision)> QueuedCollisionAsync(
        IReadOnlyList<TorrentTarget> vacant,
        CheckedClientDestination destination,
        IntegrationConnectionEntity client,
        IDownloadClient downloadClient,
        IntegrationConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        var listed = await CallAsync(client, "destination's jobs", () => downloadClient.ListDestinationJobsAsync(settings, destination.Destination.Key, cancellationToken));
        if (listed?.Value is not { } jobs)
            return (false, null);

        var pairs = client.PathMappings
            .OrderBy(mapping => mapping.Position)
            .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))
            .ToList();
        var complete = true;
        var placed = new List<(ClientJob Job, NormalizedPath? Save, NormalizedPath? Content)>();
        foreach (var job in jobs)
        {
            if (job.ServiceContentPath is not { } servicePath)
            {
                // No content path: a magnet still fetching its metadata, whose files nobody can tell yet.
                complete = false;
                continue;
            }

            var content = AppPathOf(servicePath, pairs);
            var save = job.ServiceSavePath is { } serviceSave ? AppPathOf(serviceSave, pairs) : null;
            if (content is null && save is null)
                continue;

            var exact = vacant.FirstOrDefault(target =>
                (content is not null && target.Path.IsSameAs(content))
                || (save is not null && target.Path.IsSameAs(save.Append([job.Name]))));
            if (exact is not null)
                return (true, new Collision(exact, CollisionReason.QueuedTorrent, job.Name));
            placed.Add((job, save, content));
        }

        var asked = 0;
        foreach (var (job, save, content) in placed)
        {
            var inside = vacant.Where(target => MayWrite(target.Path, save, content)).ToList();
            if (inside.Count == 0)
                continue;
            if (++asked > MaximumJobsAskedForFiles)
            {
                complete = false;
                break;
            }

            var jobFiles = await CallAsync(client, "job files", () => downloadClient.ListJobFilesAsync(settings, job.ClientJobId, cancellationToken));
            if (jobFiles is { Status: IntegrationCallStatus.NotFound })
                continue; // Removed since it was listed: it writes nothing.
            if (jobFiles?.Value is not { Count: > 0 } listedFiles)
            {
                // Couldn't be read, or no files yet (no metadata): unknown, but another torrent may still refuse.
                complete = false;
                continue;
            }

            // Below its save path when the client says it; else either the content's own folder (a torrent with one)
            // or the folder holding it may be meant, and a collision under either counts.
            NormalizedPath[] saveFolders = save is not null ? [save]
                : content!.Segments.Count > 0 ? [content with { Segments = [.. content.Segments.SkipLast(1)] }, content]
                : [content];
            foreach (var target in inside)
            {
                var writes = listedFiles.Any(file =>
                    saveFolders.Any(saveFolder => saveFolder.Append(file.RelativePath.Split('/')).IsSameAs(target.Path)));
                if (writes)
                    return (true, new Collision(target, CollisionReason.QueuedTorrent, job.Name));
            }
        }

        return (complete, null);
    }

    /// <summary>
    /// Whether a torrent saving to <paramref name="save"/>, its content now at <paramref name="content"/>, may write
    /// <paramref name="target"/>: inside its save path, and either its own folder or file there — under any content
    /// layout, the first name below the save path —, or a torrent with no folder of its own there (its content is the
    /// save path), or one still in the folder for incomplete torrents, which can't be told from its path. Without a
    /// save path, inside its content's folder.
    /// </summary>
    private static bool MayWrite(NormalizedPath target, NormalizedPath? save, NormalizedPath? content)
    {
        if (save is null)
            return content is not null && target.IsUnder(content);
        if (!target.IsUnder(save) || target.IsSameAs(save))
            return false;
        if (content is null || !content.IsUnder(save) || content.IsSameAs(save))
            return true;
        return target.IsUnder(save.Append([content.Segments[save.Segments.Count]]));
    }

    /// <summary>A path the client reports, as a path inside the app; <c>null</c> when no mapping covers it.</summary>
    private static NormalizedPath? AppPathOf(string servicePath, IReadOnlyList<PathMappingPair> pairs) =>
        PathMapper.Map(servicePath, pairs) is { HasAppPath: true, AppPath: { } appPath } && NormalizedPath.TryParse(appPath, out var parsed, out _)
            ? parsed
            : null;

    /// <summary>One read of the client that never throws but for a bug, which leaves the check unable to say,
    /// never the send without its outcome. Error, with the type and the stack, never the message.</summary>
    private async Task<IntegrationCallResult<T>?> CallAsync<T>(IntegrationConnectionEntity client, string call, Func<Task<IntegrationCallResult<T>>> read)
        where T : class
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                "The overwrite check's read of the {Call} through integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                call,
                client.Id,
                client.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return null;
        }
    }

    /// <summary>
    /// The sentence a refused send ends with: the first file it would write over, by its path below the
    /// destination's folder, why, and how many more of the release's files land on a file already there — each
    /// file once, however many of its places do (MAM-30: a file's <c>.part</c> place is that file too).
    /// </summary>
    /// <remarks>
    /// MAM-47 (ADR 0042) — a sentence per shape (the destination or the category, with or without more files, counted
    /// on <c>count</c>), the why an independent clause after its colon, nested as a message of its own.
    /// </remarks>
    private static Message RefusalOf(IntegrationConnectionEntity client, DestinationWording wording, NormalizedPath folder, IReadOnlyList<Collision> collisions)
    {
        var first = collisions[0];
        var name = string.Join('/', first.Target.Path.SegmentsAfter(folder));
        var why = first.Reason switch
        {
            CollisionReason.DifferentSize => Message.Of("downloads.overwrite.different-size-clause"),
            CollisionReason.DifferentContent => Message.Of("downloads.overwrite.different-content-clause"),
            CollisionReason.NotAFile => Message.Of("downloads.overwrite.not-a-file-clause"),
            CollisionReason.Changed => Message.Of("downloads.overwrite.changed-clause"),
            CollisionReason.QueuedTorrent => Message.Of("downloads.overwrite.queued-torrent-clause", ("torrent", first.JobName!)),
            CollisionReason.AlreadyThere => Message.Of("downloads.overwrite.already-there-clause"),
            _ => Message.Of("downloads.overwrite.unreadable-clause"),
        };
        var more = collisions.Select(collision => (object)collision.Target.File).Distinct(ReferenceEqualityComparer.Instance).Count() - 1;
        return more == 0
            ? wording.Sentence(
                "downloads.overwrite.refused-destination", "downloads.overwrite.refused-category", ("client", client.Name), ("path", name), ("why", why))
            : wording.Sentence(
                "downloads.overwrite.refused-destination-more",
                "downloads.overwrite.refused-category-more",
                ("client", client.Name),
                ("path", name),
                ("why", why),
                ("count", more));
    }

    /// <summary>
    /// MAM-24 — the sentence a replacement's strict check refuses with when it couldn't look at every place the release
    /// would write: nothing reached the client, and the owner picks Keep both himself (Vasyl's decision of 2026-10-09).
    /// </summary>
    /// <remarks>
    /// MAM-47 (ADR 0042) — the why in brackets an independent clause, nested. A magnet is never one: a replacement whose
    /// release turns out a magnet is refused by <see cref="DownloadSender"/> before it is checked, and nothing else checks
    /// strictly, so its sentence was dead and is gone (design §10.1).
    /// </remarks>
    private static Message UncheckedRefusalOf(DestinationWording wording, DownloadOverwriteCheck check)
    {
        var why = check switch
        {
            DownloadOverwriteCheck.NotCheckedMagnet =>
                throw new UnreachableException("A replacement whose release is a magnet is refused before its overwrite check."),
            DownloadOverwriteCheck.NotCheckedFileList => Message.Of("downloads.overwrite.unchecked.file-list-clause"),
            DownloadOverwriteCheck.NotCheckedFolderUnknown => Message.Of("downloads.overwrite.unchecked.folder-unknown-clause"),
            DownloadOverwriteCheck.NotCheckedFolderOutsideMediaFolders => Message.Of("downloads.overwrite.unchecked.outside-media-folders-clause"),
            DownloadOverwriteCheck.NotCheckedClientJobs => wording.IsFolder
                ? Message.Of("downloads.overwrite.unchecked.client-jobs-folder-clause")
                : Message.Of("downloads.overwrite.unchecked.client-jobs-category-clause"),
            _ => Message.Of("downloads.overwrite.unchecked.file-name-clause"),
        };
        return wording.Sentence("downloads.overwrite.unchecked-destination", "downloads.overwrite.unchecked-category", ("why", why));
    }

    /// <summary>Why a place one of the release's files would be written isn't free.</summary>
    private enum CollisionReason
    {
        DifferentSize = 1,
        DifferentContent = 2,
        NotAFile = 3,
        Unreadable = 4,
        Changed = 5,
        QueuedTorrent = 6,

        /// <summary>A file of the torrent's size met once a collision already refuses the send: already there,
        /// its bytes left unread. Never the first collision, so never the sentence's reason.</summary>
        NotCompared = 7,

        /// <summary>MAM-24 — a replacement's strict check: a file is there, whatever its bytes.</summary>
        AlreadyThere = 8,
    }

    /// <summary>One place a release's file would be written over: the place, why, and the torrent that will write
    /// there when it is one the client already has.</summary>
    private sealed record Collision(TorrentTarget Target, CollisionReason Reason, string? JobName);
}
