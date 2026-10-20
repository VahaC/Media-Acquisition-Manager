using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — check 1 of a replacement's new files (ADR 0035): the download client's own word, read fresh, that the
/// download finished without an error and that every new file of it is complete. Pure: the verifier asks the client and
/// stores what it said.
/// </summary>
/// <remarks>
/// <para>
/// Fresh, never the stored download rows: the poller stops reading a job once it is downloaded, so its file rows can be
/// a pass older than the finish, and an error or a missing file the client reports later is never seen there.
/// </para>
/// <para>
/// A new file is one the client downloads (wanted) with a video file's extension (<see cref="MediaFileNames"/>): a
/// skipped file is no new copy, and subtitles or an <c>.nfo</c> never replace anything. 100 % for the download alone
/// doesn't say which file is complete in a pack, so each new file must be complete by the client's word; one it says
/// nothing of can't be confirmed. Each reason names the client by its name, as the download's own reasons do, and is a
/// message whose template in <c>replacements.json</c> quotes it (MAM-47, ADR 0042).
/// </para>
/// </remarks>
public static class NewFileCompleteness
{
    /// <summary>
    /// What <paramref name="job"/>, the client's job the download was matched to now — <c>null</c> when the client lists
    /// it no more — says, the client being called <paramref name="clientName"/>:
    /// <see cref="NewFileCompletenessStatus.Complete"/> when it is seeding or complete at a progress of 1 with its files
    /// in its save folder, so its files may be read (<see cref="OfFiles"/>); not yet while it is queued, downloading,
    /// paused, in a state this build doesn't know (moving its files) or still keeps its files elsewhere; the owner's
    /// review when it is gone or reports an error.
    /// </summary>
    public static NewFileCompletenessResult OfJob(ClientJob? job, string clientName)
    {
        ArgumentNullException.ThrowIfNull(clientName);
        var client = MessageText.Clean(clientName);
        if (job is null)
            return NewFileCompletenessResult.Review(Message.Of("replacements.completeness.job-gone", ("client", client)));

        return job.State switch
        {
            ClientJobState.Errored => NewFileCompletenessResult.Review(Message.Of("replacements.completeness.job-errored", ("client", client))),
            // A state a later client adds, or qBittorrent's "moving": never guessed into a finished one.
            ClientJobState.Unknown => NewFileCompletenessResult.Wait(Message.Of("replacements.completeness.job-state-unknown", ("client", client))),
            // A finished state short of 1 is a re-check (DownloadJobFollowing): not finished either.
            ClientJobState.Seeding or ClientJobState.Completed when job.Progress >= 1 => InSaveFolder(job)
                ? NewFileCompletenessResult.Finished
                : NewFileCompletenessResult.Wait(Message.Of("replacements.completeness.job-outside-save-folder", ("client", client))),
            _ => NewFileCompletenessResult.Wait(Message.Of("replacements.completeness.job-unfinished", ("client", client))),
        };
    }

    /// <summary>
    /// What <paramref name="files"/>, every file the client <paramref name="clientName"/> lists for a finished download in
    /// its order, says: <see cref="NewFileCompletenessStatus.Complete"/> with the new video files when each is complete;
    /// the owner's review for the first, in the client's order, that isn't, or that the client says too little of, and
    /// when there is none; not yet while the client lists no file at all (a magnet's metadata still being fetched).
    /// </summary>
    public static NewFileCompletenessResult OfFiles(IReadOnlyList<ClientJobFile> files, string clientName)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(clientName);
        var client = MessageText.Clean(clientName);
        if (files.Count == 0)
            return NewFileCompletenessResult.Wait(Message.Of("replacements.completeness.files-unlisted", ("client", client)));

        var newFiles = new List<ClientNewFile>();
        for (var position = 0; position < files.Count; position++)
        {
            var file = files[position];
            if (!MediaFileNames.IsMediaFile(ReplacementText.FileNameOf(file.RelativePath)) || file.IsWanted == false)
                continue;

            var name = MessageText.Clean(file.RelativePath);
            if (file.IsWanted is null)
                return NewFileCompletenessResult.Review(Message.Of("replacements.completeness.file-wanted-unknown", ("client", client), ("file", name)));
            if (file.Progress is not { } progress)
                return NewFileCompletenessResult.Review(Message.Of("replacements.completeness.file-progress-unknown", ("client", client), ("file", name)));
            if (progress is not >= 1)
            {
                // Rounded down, so a file a byte short never reads as 100 % done.
                var percent = progress > 0 ? (int)Math.Floor(progress * 100) : 0;
                return NewFileCompletenessResult.Review(
                    Message.Of("replacements.completeness.file-unfinished", ("client", client), ("file", name), ("percent", percent)));
            }

            newFiles.Add(new ClientNewFile(position, file));
        }

        return newFiles.Count == 0
            ? NewFileCompletenessResult.Review(Message.Of("replacements.completeness.no-video-file", ("client", client)))
            : new NewFileCompletenessResult(NewFileCompletenessStatus.Complete, newFiles, null);
    }

    /// <summary>
    /// Whether the job's content is in its save folder, or the client doesn't say: while a client keeps an unfinished
    /// download in a folder of its own, or moves a finished one, the content path is elsewhere, and the files aren't yet
    /// where the client's save path and their names put them. A path the client doesn't give, or one that isn't a full
    /// path, says nothing here: the verifier can't place the files then, and says so itself.
    /// </summary>
    private static bool InSaveFolder(ClientJob job) =>
        !NormalizedPath.TryParse(job.ServiceContentPath, out var content, out _)
        || !NormalizedPath.TryParse(job.ServiceSavePath, out var save, out _)
        || content.IsUnder(save);
}
