using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// MAM-24 — why a copy can't be ticked (ADR 0032, ADR 0033; R3), read once per path whichever works list it.
public sealed partial class ReplacementCandidateReader
{
    /// <summary>The order a copy's reasons are checked in, the first that applies winning; the last three are the
    /// disk's, read only for a copy every stored check let through.</summary>
    private static readonly ReplacementCopyUnavailableReason[] ReasonOrder =
    [
        ReplacementCopyUnavailableReason.NoAppPath,
        ReplacementCopyUnavailableReason.FolderNotTicked,
        ReplacementCopyUnavailableReason.OutsideTheClaim,
        ReplacementCopyUnavailableReason.AlreadyScheduled,
        ReplacementCopyUnavailableReason.NotInspected,
        ReplacementCopyUnavailableReason.SharedWithUnavailableWork,
        ReplacementCopyUnavailableReason.Missing,
        ReplacementCopyUnavailableReason.Unreadable,
        ReplacementCopyUnavailableReason.ChangedSinceInspection,
    ];

    /// <summary>
    /// Each listing as a copy, in the listings' order: its path and the stored checks first, then — once per path, and
    /// only for one that passed them and that a work able to be replaced lists — the disk's answer now, compared with
    /// the identity its last inspection read (ADR 0032: the identity at that moment is what the job keeps). Every
    /// listing at one path gets that path's strictest reason and the id of the first listing there (R3).
    /// </summary>
    private async Task<List<ReplacementCandidateCopy>> CopiesOfAsync(
        IReadOnlyList<WorkPlan> plans, IReadOnlyList<Listing> listings, ReplacementGateState gates, LibraryRows rows, CancellationToken cancellationToken)
    {
        var roots = Parsed(gates.AllowedRoots);
        var appPaths = listings.Select(listing => AppPathOf(listing.Row, rows)).ToList();
        var stored = listings.Select((listing, at) => StoredReasonOf(listing, appPaths[at], roots, gates, rows)).ToList();

        // One file, one answer: listings at one app path are one copy; one the mappings give no path stays its own.
        var groups = Enumerable.Range(0, listings.Count)
            .GroupBy(at => (Path: appPaths[at], Row: appPaths[at] is null ? listings[at].Row.Id : Guid.Empty))
            .Select(group => group.ToList())
            .ToList();
        var reasons = new ReplacementCopyUnavailableReason?[listings.Count];
        var fresh = new FileFacts?[listings.Count];
        var ids = new Guid[listings.Count];
        foreach (var group in groups)
        {
            var (reason, facts) = await ReasonOfAsync(group, plans, listings, appPaths, stored, gates, cancellationToken);
            foreach (var at in group)
            {
                reasons[at] = reason;
                fresh[at] = facts;
                ids[at] = listings[group[0]].Row.Id;
            }
        }

        return
        [
            .. listings.Select((listing, at) => new ReplacementCandidateCopy(
                ids[at],
                FileOf(listing.Row, rows) with { Id = ids[at] },
                listing.Row,
                listing.Version,
                listing.WorkId,
                plans[listing.WorkIndex].Match.WorkId,
                listing.SeasonNumber,
                listing.EpisodeNumber,
                appPaths[at],
                // MAM-37 — the Duplicates page's rule: the file's name and its version's label, the work's own titles
                // and its series' taken out.
                CopyEditions.Of(listing.Row.ServicePath, [listing.Version.EditionLabel], [.. CopyEditions.TitlesOfWork(listing.WorkId, rows.Titles, rows.PositionsByEpisode)]),
                DynamicRangesOf(listing.Row, rows),
                fresh[at],
                reasons[at])),
        ];
    }

    /// <summary>
    /// One path's reason: the strictest stored one of its listings; else, where a work that can't be replaced lists it
    /// too, that work's refusal; else the disk's answer, read here once.
    /// </summary>
    private async Task<(ReplacementCopyUnavailableReason? Reason, FileFacts? Fresh)> ReasonOfAsync(
        IReadOnlyList<int> group,
        IReadOnlyList<WorkPlan> plans,
        IReadOnlyList<Listing> listings,
        IReadOnlyList<NormalizedPath?> appPaths,
        IReadOnlyList<ReplacementCopyUnavailableReason?> stored,
        ReplacementGateState gates,
        CancellationToken cancellationToken)
    {
        var strictest = group
            .Select(at => stored[at])
            .OfType<ReplacementCopyUnavailableReason>()
            .OrderBy(reason => Array.IndexOf(ReasonOrder, reason))
            .Cast<ReplacementCopyUnavailableReason?>()
            .FirstOrDefault();
        if (strictest is not null)
            return (strictest, null);

        var listedByAvailableWork = group.Any(at => plans[listings[at].WorkIndex].Unavailable is null);
        // A copy only works that can't be replaced list is never ticked: their own reason says why, and the disk is
        // left alone.
        if (!listedByAvailableWork)
            return (null, null);
        // ADR 0032 — a title alone, or ids that disagree, refuse every copy of that work, whichever other work lists it.
        if (group.Any(at => plans[listings[at].WorkIndex].Unavailable is not null))
            return (ReplacementCopyUnavailableReason.SharedWithUnavailableWork, null);

        var described = await files.DescribeAsync(appPaths[group[0]]!.ToString(), gates.AllowedRoots, cancellationToken);
        if (described.Status == FileInventoryStatus.NotFound)
            return (ReplacementCopyUnavailableReason.Missing, null);
        if (described is not { Status: FileInventoryStatus.Found, Value: { } facts })
            return (ReplacementCopyUnavailableReason.Unreadable, null);
        return group.All(at => IsSameIdentity(listings[at].Row, facts))
            ? (null, facts)
            : (ReplacementCopyUnavailableReason.ChangedSinceInspection, facts);
    }

    /// <summary>
    /// The first of the stored checks that fails, in <see cref="ReasonOrder"/>: a path in the app inside an allowed root;
    /// every library folder holding it ticked (R3); a TV copy inside the claim; no replacement not yet ended holding it,
    /// by path or by the identity its inspection read; and an identity the app read.
    /// </summary>
    private static ReplacementCopyUnavailableReason? StoredReasonOf(
        Listing listing, NormalizedPath? appPath, IReadOnlyList<NormalizedPath> roots, ReplacementGateState gates, LibraryRows rows)
    {
        if (appPath is null || !roots.Any(appPath.IsUnder))
            return ReplacementCopyUnavailableReason.NoAppPath;
        if (!gates.IsInTickedFolder(appPath))
            return ReplacementCopyUnavailableReason.FolderNotTicked;
        if (listing.OutsideTheClaim)
            return ReplacementCopyUnavailableReason.OutsideTheClaim;
        if (rows.HeldPaths.Any(held => held.IsSameAs(appPath))
            || (listing.Row is { DeviceId: { } deviceId, Inode: { } inode } && rows.HeldIdentities.Contains((deviceId, inode))))
            return ReplacementCopyUnavailableReason.AlreadyScheduled;
        // R3 — only the identity counts here: a reading the media inspector failed on leaves the copy tickable, its
        // dynamic range unknown.
        if (listing.Row is not
            {
                InspectionStatus: FileInspectionStatus.Inspected,
                DeviceId: not null,
                Inode: not null,
                ReadSizeBytes: not null,
                ReadModifiedUtc: not null,
            })
            return ReplacementCopyUnavailableReason.NotInspected;
        return null;
    }

    /// <summary>Whether the disk's <paramref name="facts"/> are the identity <paramref name="row"/>'s last inspection
    /// read: device, inode, size and modification time, to the tick.</summary>
    private static bool IsSameIdentity(PhysicalFileEntity row, FileFacts facts) =>
        facts.DeviceId == row.DeviceId
        && facts.Inode == row.Inode
        && facts.SizeBytes == row.ReadSizeBytes
        && facts.ModifiedUtc == row.ReadModifiedUtc;

    /// <summary>Where the app finds the file now, through its library's mappings (ADR 0003); <c>null</c> when they give
    /// it no path.</summary>
    private static NormalizedPath? AppPathOf(PhysicalFileEntity row, LibraryRows rows) =>
        PathMapper.Map(row.ServicePath, PairsOf(row, rows)) is { HasAppPath: true, AppPath: { } appPath }
        && NormalizedPath.TryParse(appPath, out var parsed, out _)
            ? parsed
            : null;

    private static IReadOnlyList<PathMappingPair> PairsOf(PhysicalFileEntity row, LibraryRows rows) =>
        row.SourceConnectionId is { } connectionId ? rows.PathMappings.GetValueOrDefault(connectionId) ?? [] : [];

    /// <summary>The file as the Library page shows it to the owner.</summary>
    private LibraryFileDto FileOf(PhysicalFileEntity row, LibraryRows rows)
    {
        var reading = row.MediaInspectionId is { } readingId ? rows.Readings.GetValueOrDefault(readingId) : null;
        return mapper.ToDto(
            row,
            PairsOf(row, rows),
            row.LibraryLocationId is { } location ? rows.LocationNames.GetValueOrDefault(location) : null,
            reading,
            reading is null ? [] : [.. rows.Tracks[reading.Id]],
            forOwner: true);
    }

    /// <summary>MAM-9 — the dynamic ranges ffprobe measured in the file's video, each once, in their order; none unless
    /// the last look read the media and the reading is no cached failure.</summary>
    private static IReadOnlyList<DynamicRange> DynamicRangesOf(PhysicalFileEntity row, LibraryRows rows) =>
        row is { InspectionStatus: FileInspectionStatus.Inspected, MediaInspectionId: { } readingId }
        && rows.Readings.GetValueOrDefault(readingId) is { Error: null } reading
            ? [.. rows.Tracks[reading.Id]
                .Where(track => track.Kind == MediaTrackKind.Video)
                .Select(track => track.DynamicRange)
                .OfType<DynamicRange>()
                .Distinct()
                .Order()]
            : [];
}
