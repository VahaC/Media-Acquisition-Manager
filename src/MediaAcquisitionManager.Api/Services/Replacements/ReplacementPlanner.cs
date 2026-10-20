using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Api.Services.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementPlanner"/>
/// <remarks>
/// <para>
/// In this order, the first that fails answering: the switch, the release's stored payload kind, a matched work; then
/// every ticked id a copy the release offers and every confirmation one that a work needs (400); then each ticked copy's
/// work and the copy itself able to be replaced, in the dialog's order (409, naming the file); then every confirmation a
/// ticked copy's work needs ticked, and every one ticked a copy of its work ticked (400). A file two libraries report is
/// one copy under both works (R3): ticking it ticks it under each, so each one's reason holds and each one's
/// confirmation is needed — and one id is one path, so no two ticks can name one file.
/// </para>
/// <para>
/// Every 400 ends with the dialog's next step: it reads the list again after a refusal (R4), so the owner sees what
/// changed beside the copies.
/// </para>
/// </remarks>
public sealed class ReplacementPlanner(
    IReplacementGates gates,
    IReleaseMatchReader matches,
    IReplacementCandidateReader candidates,
    ILibraryLinkReader links,
    ILogger<ReplacementPlanner> logger) : IReplacementPlanner
{
    // MAM-47 (ADR 0042) — the refusals are messages the controller renders in the request's language. The "read again"
    // sentence that ends a refusal of the ticks is part of each such key: a whole sentence per shape, never a fragment.

    private static readonly Message SwitchedOffError = Message.Of("replacements.plan.switched-off");

    private static readonly Message MagnetError = Message.Of("replacements.plan.magnet");

    private static readonly Message NoMatchedWorkError = Message.Of("replacements.plan.no-matched-work");

    private static readonly Message NotACopyError = Message.Of("replacements.plan.not-a-copy");

    private static readonly Message NotConfirmableError = Message.Of("replacements.plan.not-confirmable");

    public async Task<ReplacementPlanResult> PlanAsync(CallerRelease found, ReplacementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(request);
        var release = found.Release;

        // Read fresh, never cached (ADR 0033): the gates as they are when the schedule comes, not as the dialog read them.
        var gate = await gates.ReadAsync(cancellationToken);
        if (!gate.IsEnabled)
            return Refused(release.Id, ReplacementPlanStatus.Unavailable, "SwitchedOff", SwitchedOffError);
        // The stored kind only: a gateway's link turns out a magnet when it is fetched, after the job is written, which the
        // sender's strict mode refuses (§3.5).
        if (release.PayloadKind == ReleasePayloadKind.Magnet)
            return Refused(release.Id, ReplacementPlanStatus.Unavailable, "Magnet", MagnetError);

        // The owner's view of the works, as the dialog read them: the route is the owner's alone.
        var matched = await matches.MatchAsync(found, forOwner: true, cancellationToken);
        if (matched.Release.LibraryMatches.Count == 0)
            return Refused(release.Id, ReplacementPlanStatus.Unavailable, "NoMatchedWork", NoMatchedWorkError);

        var works = await candidates.ReadAsync(matched, gate, cancellationToken);
        // Every listing of every copy, in the dialog's order: the works' order, then each work's copies.
        List<(ReplacementCandidateWork Work, ReplacementCandidateCopy Copy)> listings =
            [.. works.SelectMany(work => work.AllCopies.Select(copy => (work, copy)))];

        var listed = listings.Select(listing => listing.Copy.Id).ToHashSet();
        if (request.FileIds.Any(id => !listed.Contains(id)))
            return Refused(release.Id, ReplacementPlanStatus.Invalid, "NotACopy", NotACopyError);
        var confirmable = works.Where(work => work.NeedsConfirmation).Select(work => work.WorkId).ToHashSet();
        if (request.ConfirmedWorkIds.Any(id => !confirmable.Contains(id)))
            return Refused(release.Id, ReplacementPlanStatus.Invalid, "NotConfirmable", NotConfirmableError);

        var ticked = request.FileIds.ToHashSet();
        var tickedListings = listings.Where(listing => ticked.Contains(listing.Copy.Id)).ToList();
        foreach (var (work, copy) in tickedListings)
        {
            // The work's reason first: a copy only works that can't be replaced list was never looked at on the disk.
            if (work.Unavailable is { } workReason)
                return Refused(release.Id, ReplacementPlanStatus.Unavailable, workReason.ToString(), WorkReasonError(workReason, work, copy));
            if (copy.Unavailable is { } copyReason)
                return Refused(release.Id, ReplacementPlanStatus.Unavailable, copyReason.ToString(), CopyReasonError(copyReason, copy));
        }

        // ADR 0032 — a match by title and year is replaced only on the owner's tick, each work's own, whichever work's
        // listing of a shared file he ticked it under. MAM-47 — the consent words nested whole in the sentence that asks
        // for the tick (a clause of their own keys).
        var confirmed = request.ConfirmedWorkIds.ToHashSet();
        if (tickedListings.Select(listing => listing.Work).FirstOrDefault(work => work.NeedsConfirmation && !confirmed.Contains(work.WorkId))
            is { } unconfirmed)
        {
            return Refused(
                release.Id,
                ReplacementPlanStatus.Invalid,
                "NotConfirmed",
                Message.Of("replacements.plan.not-confirmed", ("confirmation", unconfirmed.ConfirmationText)));
        }

        // R4 — every confirmation kept is one a ticked copy needs: a retry's ticks must be the job's to be answered with it.
        var tickedWorkIds = tickedListings.Select(listing => listing.Work.WorkId).ToHashSet();
        if (works.FirstOrDefault(work => confirmed.Contains(work.WorkId) && !tickedWorkIds.Contains(work.WorkId)) is { } untickedWork)
        {
            return Refused(
                release.Id,
                ReplacementPlanStatus.Invalid,
                "ConfirmedWithoutCopy",
                NameOf(untickedWork) is { } name
                    ? Message.Of("replacements.plan.confirmed-without-copy", ("work", name), ("confirmation", untickedWork.ConfirmationText))
                    : Message.Of("replacements.plan.confirmed-without-copy-untitled", ("confirmation", untickedWork.ConfirmationText)));
        }

        List<ReplacementPlanFile> files = [.. tickedListings.Select(listing => listing.Copy).DistinctBy(copy => copy.Id).Select(FileOf)];
        var plannedWorks = works.Where(work => tickedWorkIds.Contains(work.WorkId)).DistinctBy(work => work.WorkId).ToList();
        // The ids as every reader counts them, the owner's TMDb links applied (ADR 0026): a series' its show's own.
        var linked = await links.ReadAsync([.. plannedWorks.Select(work => work.WorkId)], forOwner: true, cancellationToken);
        List<ReplacementPlanWork> planWorks =
        [
            .. plannedWorks.Select(work => new ReplacementPlanWork(
                work.WorkId,
                work.Kind,
                work.Title,
                work.Year,
                work.Match.Evidence,
                confirmed.Contains(work.WorkId),
                work.ConfirmationText,
                [.. linked[work.WorkId].Identifiers
                    .Select(identifier => new ReplacementPlanIdentifier(
                        identifier.Scheme, identifier.Value, identifier.SourceKind == ProvenanceSourceKind.Person))
                    .Distinct()])),
        ];
        return ReplacementPlanResult.Planned(new ReplacementPlan(ReplacementReleaseEditions.Of(release), files, planWorks));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One ticked copy as the job keeps it, with the identity the disk gave it while this plan read it (decision 10): the
    /// copy reader reads every copy that passed the stored checks, and refuses one whose identity changed, so a copy
    /// that can be ticked has one.
    /// </summary>
    private static ReplacementPlanFile FileOf(ReplacementCandidateCopy copy) =>
        copy is { AppPath: { } appPath, Fresh: { DeviceId: { } deviceId, Inode: { } inode } fresh }
            ? new ReplacementPlanFile(
                copy.Id,
                copy.Version.Id,
                copy.WorkId,
                copy.MatchedWorkId,
                copy.SeasonNumber,
                copy.EpisodeNumber,
                copy.Row.ServicePath,
                appPath.ToString(),
                deviceId,
                inode,
                fresh.SizeBytes,
                fresh.ModifiedUtc,
                fresh.LinkCount,
                copy.Version.EditionLabel)
            // A bug, never an outcome: nothing is written, and the request fails rather than scheduling a copy the job
            // couldn't find again by its identity.
            : throw new InvalidOperationException("A copy that can be ticked came without its path or the identity read from the disk.");

    /// <summary>Logs a refused plan — the release and a word for why, never a path or a title — and answers it.</summary>
    private ReplacementPlanResult Refused(Guid releaseId, ReplacementPlanStatus status, string why, Message error)
    {
        logger.LogInformation("The replacement planned with release {ReleaseId} was refused ({Status}): {Why}.", releaseId, status, why);
        return status == ReplacementPlanStatus.Invalid ? ReplacementPlanResult.Invalid(error) : ReplacementPlanResult.Unavailable(error);
    }

    /// <summary>A work as the sentences name it: "Alien (1979)", "Alien" without a year — its title and year as data —;
    /// <c>null</c> for a work with no title, which each sentence names by a key of its own words (MAM-47).</summary>
    private static string? NameOf(ReplacementCandidateWork work) => ReplacementConfirmation.NameOf(work.Title, work.Year);

    /// <summary>Why a ticked copy of <paramref name="work"/> can't be replaced, by the work's reason, naming the
    /// file.</summary>
    private static Message WorkReasonError(ReplacementWorkUnavailableReason reason, ReplacementCandidateWork work, ReplacementCandidateCopy copy)
    {
        var file = FileNameOf(copy);
        // A work with no title is named by words of the key's own (design §1.3: a fragment filling the frame is a shape).
        return (reason, NameOf(work)) switch
        {
            (ReplacementWorkUnavailableReason.TitleOnly, { } name) =>
                Message.Of("replacements.plan.work.title-only", ("work", name), ("file", file)),
            (ReplacementWorkUnavailableReason.TitleOnly, null) => Message.Of("replacements.plan.work.title-only-untitled", ("file", file)),
            (ReplacementWorkUnavailableReason.ConflictingIds, { } name) =>
                Message.Of("replacements.plan.work.conflicting-ids", ("work", name), ("file", file)),
            (ReplacementWorkUnavailableReason.ConflictingIds, null) => Message.Of("replacements.plan.work.conflicting-ids-untitled", ("file", file)),
            (ReplacementWorkUnavailableReason.NoClaimedSeason, { } name) =>
                Message.Of("replacements.plan.work.no-claimed-season", ("work", name), ("file", file)),
            (ReplacementWorkUnavailableReason.NoClaimedSeason, null) => Message.Of("replacements.plan.work.no-claimed-season-untitled", ("file", file)),
            (_, { } name) => Message.Of("replacements.plan.work.no-claimed-episodes", ("work", name), ("file", file)),
            (_, null) => Message.Of("replacements.plan.work.no-claimed-episodes-untitled", ("file", file)),
        };
    }

    /// <summary>Why a ticked copy can't be replaced, by its own reason, naming the file.</summary>
    private static Message CopyReasonError(ReplacementCopyUnavailableReason reason, ReplacementCandidateCopy copy)
    {
        var file = FileNameOf(copy);
        return reason switch
        {
            ReplacementCopyUnavailableReason.NoAppPath => Message.Of("replacements.plan.copy.no-app-path", ("file", file)),
            ReplacementCopyUnavailableReason.FolderNotTicked => Message.Of("replacements.plan.copy.folder-not-ticked", ("file", file)),
            ReplacementCopyUnavailableReason.OutsideTheClaim => Message.Of("replacements.plan.copy.outside-the-claim", ("file", file)),
            ReplacementCopyUnavailableReason.AlreadyScheduled => Message.Of("replacements.plan.copy.already-scheduled", ("file", file)),
            ReplacementCopyUnavailableReason.HeldByRemoval => Message.Of("replacements.plan.copy.held-by-removal", ("file", file)),
            ReplacementCopyUnavailableReason.NotInspected => Message.Of("replacements.plan.copy.not-inspected", ("file", file)),
            ReplacementCopyUnavailableReason.SharedWithUnavailableWork => Message.Of("replacements.plan.copy.shared-with-unavailable-work", ("file", file)),
            ReplacementCopyUnavailableReason.Missing => Message.Of("replacements.plan.copy.missing", ("file", file)),
            ReplacementCopyUnavailableReason.Unreadable => Message.Of("replacements.plan.copy.unreadable", ("file", file)),
            _ => Message.Of("replacements.plan.copy.changed", ("file", file)),
        };
    }

    /// <summary>The copy's file name, the end of its path as its library reported it: what the owner ticked it by.</summary>
    private static string FileNameOf(ReplacementCandidateCopy copy) => CopyEditions.FileNameOf(copy.Row.ServicePath);
}
