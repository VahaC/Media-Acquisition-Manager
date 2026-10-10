using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — checks 4 and 3 of a replacement's new files, and the pairs (ADR 0032, ADR 0035): which matched work the
/// library identified each new file as, by its registry ids alone, and which new file each ticked old copy is paired
/// with — or why the job must wait, or stop for the owner's review. Pure: the verifier reads the library and stores what
/// this says.
/// </summary>
/// <remarks>
/// <para>
/// A new file's work: a film item must carry a TMDb or IMDb id of exactly one matched film and no
/// other id of a scheme that film has; an episode item's series must be exactly one matched show by the same rule over
/// the show's ids (ADR 0017: an IMDb id among the show's, TMDb's and TVDB's of shows), at a season and an episode number.
/// An item that is no work (an extra) holds nothing up. A file of the other kind than every matched work, an episode of
/// another series, or a file of conflicting ids stops the job, paired or not. A film of another work — another id of a
/// scheme the work has an id of; the matched works being only those a copy is ticked of, a collection's other film is
/// one — is simply added once every ticked copy found its pair; until then it may be the missing pair misidentified, and
/// stops the job (Vasyl, 2026-10-10). Ids only of schemes the work has none of are compared with nothing: the file waits
/// as one without an id does, and stops the job at once only when the work has no id of those schemes at all, as one
/// confirmed by its title and year may not.
/// </para>
/// <para>
/// Each ticked copy, by its place, is paired with exactly one new file of its own work: a film's, or the episode's of its
/// series at its season and number. No such file, two of them, a copy or a file that is one half of an episode or covers
/// several, and an episode the library now lists as a film, places in another series or numbers otherwise than when it
/// was scheduled stop the job; so does a paired file whose name claims an edition the release didn't — the work's and
/// the episode's own titles taken out first, as MAM-37 reads names —, or that runs under three quarters of the copy it
/// replaces (MAM-37's measure, one way only: a longer cut passes). A new file no copy pairs with is simply added.
/// Names — a range, a half, an edition — may only stop a replacement, never let one go.
/// </para>
/// <para>
/// The verdict, in this order: a file the library doesn't list or hasn't finished reading, a new one or an episode
/// copy's, waits — until the library's wait ran out, when the first such stops the job —; then any definitive problem
/// stops it at once, the ticked copies' by their place first, then the new files'; then a file the library hasn't
/// identified yet, an episode copy's series or a new file, waits as the first did; else every copy is paired, and the
/// job is verified. "No new file is this copy's work" is no problem while a new file is still unread, unidentified or
/// of a work it can't be: that file is the reason, and the owner fixing it may pair the copy.
/// </para>
/// </remarks>
public static partial class ReplacementPairing
{
    /// <summary>
    /// The verdict on <paramref name="newFiles"/> and <paramref name="oldCopies"/> of a job that matched
    /// <paramref name="works"/> on a release claiming <paramref name="releaseEditions"/>, the library being called
    /// <paramref name="libraryName"/>; <paramref name="libraryWaitPassed"/> once
    /// <see cref="ReplacementVerificationRules.LibraryWait"/> ran out. The outcomes come in the files' and copies' order
    /// of place. Throws for a copy of a work not among <paramref name="works"/>: the job's rows are written together.
    /// </summary>
    public static ReplacementPairingResult Pair(
        IReadOnlyList<ReplacementPairingWork> works,
        IReadOnlyList<ReplacementPairingOldCopy> oldCopies,
        IReadOnlyList<ReplacementPairingNewFile> newFiles,
        IReadOnlyCollection<MediaEdition> releaseEditions,
        string libraryName,
        bool libraryWaitPassed)
    {
        ArgumentNullException.ThrowIfNull(works);
        ArgumentNullException.ThrowIfNull(oldCopies);
        ArgumentNullException.ThrowIfNull(newFiles);
        ArgumentNullException.ThrowIfNull(releaseEditions);
        ArgumentNullException.ThrowIfNull(libraryName);

        var library = ReplacementText.Quote(libraryName);
        var read = newFiles.OrderBy(file => file.Position).Select(file => Identify(file, works, library)).ToList();
        // A copy's "no new file" waits on any file that might still be it (the remarks say why).
        var unsettled = read.Any(file => file.Waiting is not null || file.Pending is not null || file.Problem is not null);
        var copies = oldCopies
            .OrderBy(copy => copy.Position)
            .Select(copy => Pair(copy, WorkOf(copy, works), read, releaseEditions, library, unsettled))
            .ToList();
        // Vasyl, 2026-10-10 — once every ticked copy found its pair, a film of another work is a collection's other film,
        // simply added as a season pack's extra episode is; until then it may be the missing pair misidentified, and stops
        // the job. No copy is paired again: "unsettled" speaks only for a copy that found none.
        if (copies.All(copy => copy.PairedNewFileId is not null))
            read = [.. read.Select(file => file.OtherFilm ? file with { Problem = null } : file)];

        // a. The library hasn't listed or finished reading every file.
        var waiting = copies.Select(copy => copy.Waiting).Concat(read.Select(file => file.Waiting)).OfType<string>().ToList();
        if (waiting.Count > 0)
            return libraryWaitPassed
                ? Result(ReplacementPairingVerdict.Review, waiting[0], read, copies, showWaiting: true, showPending: false)
                : Result(ReplacementPairingVerdict.Wait, null, read, copies, showWaiting: false, showPending: false);

        // b. A definitive problem stops the job at once.
        var problems = copies.Select(copy => copy.Problem).Concat(read.Select(file => file.Problem)).OfType<string>().ToList();
        if (problems.Count > 0)
            return Result(ReplacementPairingVerdict.Review, problems[0], read, copies, showWaiting: false, showPending: false);

        // c. A file the library hasn't identified yet: an episode copy's series, or a new file.
        var pending = copies.Select(copy => copy.Pending).Concat(read.Select(file => file.Pending)).OfType<string>().ToList();
        if (pending.Count > 0)
            return libraryWaitPassed
                ? Result(ReplacementPairingVerdict.Review, pending[0], read, copies, showWaiting: false, showPending: true)
                : Result(ReplacementPairingVerdict.Wait, null, read, copies, showWaiting: false, showPending: false);

        // d. Every copy paired, no file stops it.
        return Result(ReplacementPairingVerdict.Pass, null, read, copies, showWaiting: false, showPending: false);
    }

    /// <summary>Rules 2 and 3 for one ticked copy: its own problems, then its pair.</summary>
    private static OldCopyRead Pair(
        ReplacementPairingOldCopy copy,
        ReplacementPairingWork work,
        IReadOnlyList<NewFileRead> newFiles,
        IReadOnlyCollection<MediaEdition> releaseEditions,
        string library,
        bool unsettled)
    {
        var path = ReplacementText.Quote(copy.AppPath);
        if (work.Kind != MediaWorkKind.Series)
        {
            var films = newFiles.Where(file => file.WorkId == work.WorkId && file.File.Library.Kind == LibraryFileKind.Movie).ToList();
            return Paired(copy, work, films, path, work.Name, releaseEditions, unsettled, null, null);
        }

        if (copy is not { SeasonNumber: { } season, EpisodeNumber: { } episode })
            return new(copy, null, null, null, OldNotNumbered(library, path));

        var name = ReplacementText.FileNameOf(copy.AppPath);
        if (EpisodeRangeNames.HalfOf(name) is { } half)
            return new(copy, null, null, null, Half(path, half));
        if (EpisodeRangeNames.RangeOf(name) is { CoversSeveral: true } named)
        {
            var span = new EpisodeSpan(season, Math.Min(episode, named.FirstEpisode), Math.Max(episode, named.LastEpisode));
            return new(copy, null, null, null, OldCoversSeveral(path, span));
        }

        // The old item's own word: its kind, its series, its numbers, and the end number of a file of several episodes.
        // Not listed or not read yet is a wait, never a problem: the library may make the item anew for a moment.
        string? waiting = null;
        string? pending = null;
        switch (copy.Library)
        {
            case null or { Kind: null }:
                waiting = OldNotListed(library, path);
                break;
            case { Kind: LibraryFileKind.Movie }:
                return new(copy, null, null, null, OldListedAsFilm(library, path));
            case { Kind: not LibraryFileKind.Episode }:
                return new(copy, null, null, null, OldListedAsNoEpisode(library, path));
            case { Refreshed: false }:
                waiting = OldNotRefreshed(library, path);
                break;
            case { } reading:
                // ADR 0017 — the library alone says what an item is, and it says so now, not when the copy was
                // scheduled: the owner may have re-identified or renumbered the old item since. Its series must still be
                // the copy's show by a new file's rule, and its numbers the copy's; a series without an id waits as a new
                // file's does.
                var old = $"the old copy {path}";
                var series = reading.SeriesItemId is null ? Identification.WithoutId : Identify(reading.SeriesIdentifiers, [work], SeriesSchemes);
                if (series.NoId)
                    pending = EpisodeWithoutSeries(library, old, [work]);
                else if (series.Work is null)
                {
                    var unidentified = Unidentified(series, library, old, reading.SeriesIdentifiers, SeriesSchemes, [work], ofSeries: true);
                    if (unidentified.Problem is { } problem)
                        return new(copy, null, null, null, problem);
                    pending = unidentified.Pending;
                }

                if (reading is not { SeasonNumber: { } nowSeason, EpisodeNumber: { } nowEpisode } || nowSeason != season || nowEpisode != episode)
                    return new(copy, null, null, null, OldRenumbered(library, path, reading.SeasonNumber, reading.EpisodeNumber, season, episode));
                // The span in the library's numbering now — the copy's own, as just checked —, never half of each.
                if (reading.EpisodeNumberEnd is { } end && end > nowEpisode)
                    return new(copy, null, null, null, OldCoversSeveral(path, new EpisodeSpan(nowSeason, nowEpisode, end)));
                break;
        }

        var candidates = newFiles
            .Where(file => file.WorkId == work.WorkId && file.Covers is { } covers && covers.Holds(season, episode))
            .ToList();
        var place = EpisodeName(work.Name, season, episode);
        if (candidates.FirstOrDefault(file => file.Covers!.Several) is { } several)
            return new(copy, null, waiting, pending, NewCoversSeveral(ReplacementText.Quote(several.File.AppPath), several.Covers!, path));
        var halved = candidates
            .Select(file => (File: file, Half: EpisodeRangeNames.HalfOf(file.Name)))
            .FirstOrDefault(file => file.Half is not null);
        if (halved is { File: { } halfFile, Half: { } letter })
            return new(copy, null, waiting, pending, NewHalf(ReplacementText.Quote(halfFile.File.AppPath), letter, path));
        return Paired(copy, work, candidates, path, place, releaseEditions, unsettled, waiting, pending);
    }

    /// <summary>The pair of a copy with its <paramref name="candidates"/>, the new files of its work at its place:
    /// exactly one, then its edition and running time (rule 3).</summary>
    private static OldCopyRead Paired(
        ReplacementPairingOldCopy copy,
        ReplacementPairingWork work,
        IReadOnlyList<NewFileRead> candidates,
        string path,
        string what,
        IReadOnlyCollection<MediaEdition> releaseEditions,
        bool unsettled,
        string? waiting,
        string? pending)
    {
        switch (candidates.Count)
        {
            case 0:
                return new(copy, null, waiting, pending, unsettled ? null : NoNewFile(what, path));
            case > 1:
                return new(copy, null, waiting, pending, TwoNewFiles(what, [.. candidates.Select(file => ReplacementText.Quote(file.File.AppPath))]));
        }

        var paired = candidates[0];
        var newPath = ReplacementText.Quote(paired.File.AppPath);
        // MAM-37's rule: every title of the work, and of an episode its own, is taken out of the name first.
        var claimed = EditionLabels.Read(paired.Name, work.Titles.Concat(copy.EpisodeTitles));
        if (claimed.Any(edition => !releaseEditions.Contains(edition)))
            return new(copy, paired.File.Id, waiting, pending, OtherEdition(newPath, claimed, releaseEditions));
        if (paired.File.DurationMilliseconds is { } newDuration
            && copy.DurationMilliseconds is { } oldDuration
            && RunningTimeSpread.IsWide(newDuration, oldDuration))
            return new(copy, paired.File.Id, waiting, pending, TooShort(newPath, newDuration, oldDuration));
        return new(copy, paired.File.Id, waiting, pending, null);
    }

    private static ReplacementPairingWork WorkOf(ReplacementPairingOldCopy copy, IReadOnlyList<ReplacementPairingWork> works) =>
        works.FirstOrDefault(work => work.WorkId == copy.MatchedWorkId)
            ?? throw new ArgumentException($"The old copy at place {copy.Position} is of a work the job didn't match.", nameof(works));

    /// <summary>
    /// The outcomes: each row's definitive problem, or — when the verdict stops the job for what was still missing once the
    /// library's wait ran out — that missing thing's sentence.
    /// </summary>
    private static ReplacementPairingResult Result(
        ReplacementPairingVerdict verdict,
        string? reason,
        IReadOnlyList<NewFileRead> newFiles,
        IReadOnlyList<OldCopyRead> copies,
        bool showWaiting,
        bool showPending) =>
        new(
            verdict,
            reason,
            [
                .. newFiles.Select(file => new ReplacementNewFileOutcome(
                    file.File.Id,
                    file.WorkId,
                    file.Problem ?? (showWaiting ? file.Waiting : null) ?? (showPending ? file.Pending : null))),
            ],
            [
                .. copies.Select(copy => new ReplacementOldCopyOutcome(
                    copy.Copy.Id,
                    copy.PairedNewFileId,
                    copy.Problem ?? (showWaiting ? copy.Waiting : null) ?? (showPending ? copy.Pending : null))),
            ]);

    /// <summary>What rule 1 made of one new file.</summary>
    /// <param name="File">The file.</param>
    /// <param name="Name">Its own name, which names are read from.</param>
    /// <param name="WorkId">The matched work the library identified it as.</param>
    /// <param name="Covers">An identified episode's season and the episodes it covers.</param>
    /// <param name="Waiting">The library doesn't list it or hasn't finished reading it (verdict a).</param>
    /// <param name="Pending">The library hasn't identified or numbered it yet (verdict c).</param>
    /// <param name="Problem">Why it stops the job (verdict b).</param>
    /// <param name="OtherFilm">The library identified it as a film of another work, which its problem says: simply added
    /// instead once every ticked copy found its pair.</param>
    private sealed record NewFileRead(
        ReplacementPairingNewFile File,
        string Name,
        Guid? WorkId,
        EpisodeSpan? Covers,
        string? Waiting,
        string? Pending,
        string? Problem,
        bool OtherFilm);

    /// <summary>What rules 2 and 3 made of one ticked copy.</summary>
    /// <param name="Copy">The copy.</param>
    /// <param name="PairedNewFileId">The one new file of its work.</param>
    /// <param name="Waiting">The library doesn't list its item or hasn't finished reading it (verdict a).</param>
    /// <param name="Pending">The library gives its item's series no id it can be told by yet (verdict c).</param>
    /// <param name="Problem">Why it stops the job (verdict b).</param>
    private sealed record OldCopyRead(ReplacementPairingOldCopy Copy, Guid? PairedNewFileId, string? Waiting, string? Pending, string? Problem);

    /// <summary>The episodes a file covers within one season, from <see cref="First"/> to <see cref="Last"/>.</summary>
    private sealed record EpisodeSpan(int Season, int First, int Last)
    {
        public bool Several => Last > First;

        public bool Holds(int season, int episode) => Season == season && episode >= First && episode <= Last;
    }
}
