using System.Globalization;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Api.Services.Search;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementCandidateReader"/>
/// <remarks>
/// <para>
/// A fixed handful of queries however many works, episodes and files the release claims, then one look at the disk per
/// path that passed every stored check — never one per row, and never a download client (R1: the send's plan reads
/// this same answer before anything is written, ADR 0013).
/// </para>
/// <para>
/// The copies are the Library page's: a film's versions in its order, each version's files by path; an episode's the
/// same. A file the mappings bring into the app is one copy however many works list it (R3): two libraries over one
/// folder are two works, and the strictest reason of any of them holds at its path, so a file can't be ticked through
/// a work matched by its id while it is also the copy of a work matched by its title alone (ADR 0032).
/// </para>
/// </remarks>
public sealed partial class ReplacementCandidateReader(ApplicationDbContext db, IFileInventory files, ILibraryMapper mapper)
    : IReplacementCandidateReader
{
    public async Task<IReadOnlyList<ReplacementCandidateWork>> ReadAsync(
        MatchedRelease matched, ReplacementGateState gates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matched);
        ArgumentNullException.ThrowIfNull(gates);
        var release = matched.Release;
        if (release.LibraryMatches.Count == 0)
            return [];

        var isTv = release.Release.Kind == ReleaseKind.Tv;
        var claimed = isTv ? release.ClaimedWork.Episodes : null;
        var rows = await LoadAsync([.. release.LibraryMatches.Select(match => match.WorkId).Distinct()], isTv, claimed, cancellationToken);
        var works = matched.Works.Works.ToDictionary(work => work.Work.Id);
        List<WorkPlan> plans = [.. release.LibraryMatches.Select(match => PlanOf(match, works[match.WorkId], isTv, claimed, rows))];
        var listings = ListingsOf(plans, claimed, rows);
        var copies = await CopiesOfAsync(plans, listings, gates, rows, cancellationToken);
        return Assemble(plans, listings, copies, rows);
    }

    // ── reads ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every row the copies are read from, batched: one query each for the claimed places of the matched series'
    /// episodes, every place of those episodes, the versions, their files, the titles, the libraries, the mappings, the
    /// readings with their tracks, and the files of the replacements not yet ended.
    /// </summary>
    private async Task<LibraryRows> LoadAsync(
        List<Guid> matchedIds, bool isTv, ClaimedEpisodes? claimed, CancellationToken cancellationToken)
    {
        // MAM-32 — an episode is claimed by its places (ADR 0017): every place the matched series give their episodes,
        // then those the title claims. A TV title claiming no season claims none.
        List<MediaEpisodePositionEntity> claimedPositions = isTv && claimed is not null
            ? [.. (await db.MediaEpisodePositions.AsNoTracking()
                    .Where(position => position.SeriesWorkId != null && EF.Parameter(matchedIds).Contains(position.SeriesWorkId.Value))
                    .ToListAsync(cancellationToken))
                .Where(position => claimed.Holds(position.SeasonNumber, position.EpisodeNumber))]
            : [];
        var episodeIds = claimedPositions.Select(position => position.MediaWorkId).Distinct().ToList();
        // Every place of those episodes, under any series: a version is tied to its own library item's place (R3), and an
        // episode's series' titles are no edition words.
        var episodePositions = await db.MediaEpisodePositions.AsNoTracking()
            .Where(position => EF.Parameter(episodeIds).Contains(position.MediaWorkId))
            .ToListAsync(cancellationToken);

        var copyWorkIds = isTv ? episodeIds : matchedIds;
        var versions = await db.MediaVersions.AsNoTracking()
            .Where(version => EF.Parameter(copyWorkIds).Contains(version.MediaWorkId))
            .ToListAsync(cancellationToken);
        var versionIds = versions.Select(version => version.Id).ToList();
        var physicalFiles = await db.PhysicalFiles.AsNoTracking()
            .Where(file => file.MediaVersionId != null && EF.Parameter(versionIds).Contains(file.MediaVersionId.Value))
            .ToListAsync(cancellationToken);

        var titleIds = copyWorkIds
            .Concat(matchedIds)
            .Concat(episodePositions.Select(position => position.SeriesWorkId).OfType<Guid>())
            .Distinct()
            .ToList();
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => EF.Parameter(titleIds).Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var locationIds = physicalFiles.Select(file => file.LibraryLocationId).OfType<Guid>().Distinct().ToList();
        var locationNames = await db.LibraryLocations.AsNoTracking()
            .Where(location => EF.Parameter(locationIds).Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, location => location.Name, cancellationToken);
        // ADR 0003 — each reporting integration's mappings as they are now, as the Library page maps its files.
        var connectionIds = physicalFiles.Select(file => file.SourceConnectionId).OfType<Guid>().Distinct().ToList();
        var pathMappings = (await db.PathMappings.AsNoTracking()
                .Where(mapping => EF.Parameter(connectionIds).Contains(mapping.IntegrationConnectionId))
                .ToListAsync(cancellationToken))
            .GroupBy(mapping => mapping.IntegrationConnectionId)
            .ToDictionary(
                integration => integration.Key,
                integration => (IReadOnlyList<PathMappingPair>)[.. integration
                    .OrderBy(mapping => mapping.Position)
                    .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))]);
        var readingIds = physicalFiles.Select(file => file.MediaInspectionId).OfType<Guid>().Distinct().ToList();
        var readings = await db.MediaInspections.AsNoTracking()
            .Where(reading => EF.Parameter(readingIds).Contains(reading.Id))
            .ToDictionaryAsync(reading => reading.Id, cancellationToken);
        var tracks = (await db.MediaInspectionTracks.AsNoTracking()
                .Where(track => EF.Parameter(readingIds).Contains(track.MediaInspectionId))
                .ToListAsync(cancellationToken))
            .ToLookup(track => track.MediaInspectionId);

        // R3 — the files of every replacement not yet ended: a copy one of them holds can't be picked again. Few rows: a
        // replacement is the owner's, one per download.
        var held = await (
                from file in db.ReplacementJobFiles.AsNoTracking()
                join job in db.ReplacementJobs.AsNoTracking() on file.ReplacementJobId equals job.Id
                where EF.Constant(ReplacementJobStates.Open).Contains(job.State)
                select new { file.AppPath, file.DeviceId, file.Inode })
            .ToListAsync(cancellationToken);

        return new LibraryRows(
            claimedPositions.ToLookup(position => position.SeriesWorkId!.Value),
            episodePositions.ToLookup(position => position.MediaWorkId),
            versions.ToLookup(version => version.MediaWorkId),
            physicalFiles.ToLookup(file => file.MediaVersionId!.Value),
            titles,
            locationNames,
            pathMappings,
            readings,
            tracks,
            [.. held.Select(file => NormalizedPath.TryParse(file.AppPath, out var path, out _) ? path : null).OfType<NormalizedPath>()],
            [.. held.Select(file => (file.DeviceId, file.Inode))]);
    }

    // ── assembly ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The works with their copies and episodes in their order, and each work's libraries and words.</summary>
    private static List<ReplacementCandidateWork> Assemble(
        IReadOnlyList<WorkPlan> plans, IReadOnlyList<Listing> listings, IReadOnlyList<ReplacementCandidateCopy> copies, LibraryRows rows)
    {
        var assembled = new List<ReplacementCandidateWork>(plans.Count);
        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var own = Enumerable.Range(0, listings.Count).Where(at => listings[at].WorkIndex == index).ToList();
            List<ReplacementCandidateCopy> filmCopies = [.. own.Where(at => listings[at].EpisodeIndex is null).Select(at => copies[at])];
            List<ReplacementCandidateEpisode> episodes =
            [
                .. plan.Episodes.Select((episode, episodeIndex) => new ReplacementCandidateEpisode(
                    episode.WorkId,
                    episode.SeasonNumber,
                    episode.EpisodeNumber,
                    episode.Title,
                    [.. own.Where(at => listings[at].EpisodeIndex == episodeIndex).Select(at => copies[at])])),
            ];
            // The libraries the listed copies are in, each once, in the Library's title order.
            List<string> libraryNames =
            [
                .. own.Select(at => listings[at].Row.LibraryLocationId is { } location ? rows.LocationNames.GetValueOrDefault(location) : null)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, LibraryTitleOrder.Comparer)
                    .ThenBy(name => name, StringComparer.Ordinal),
            ];
            assembled.Add(new ReplacementCandidateWork(
                plan.Match.WorkId,
                plan.Kind,
                plan.Title,
                plan.Year,
                plan.Match.Match,
                plan.Match.Match is { Evidence: MatchEvidence.TitleAndYear, ConflictingSchemes.Count: 0 },
                plan.ConfirmationText,
                libraryNames,
                plan.Unavailable,
                filmCopies,
                episodes));
        }

        return TellTwinsApart(assembled);
    }

    /// <summary>
    /// R3 — two works whose confirmations would read the same (one film in two libraries, two shows of one title) are
    /// told apart by their libraries: "This release is Alien (1979) in “Фільми”". The owner must see which one he
    /// confirms, and the audit row keeps the same words.
    /// </summary>
    /// <remarks>
    /// Two that still read the same — two works of one title and year in one library, one of them left without an id (the
    /// duplicates MAM-20 finds), each matched by title and year — are told apart by the name of the first copy each
    /// lists, when those differ: "… (the copy “Halloween.2018.1080p.mkv”)". Otherwise by their place among them in the
    /// dialog's order: "… (1 of 2)" — and so is a name that would make the words longer than the audit row keeps
    /// (<see cref="AuditTarget.TargetNameMaximumLength"/>), whose cut would take what tells them apart.
    /// </remarks>
    private static List<ReplacementCandidateWork> TellTwinsApart(IReadOnlyList<ReplacementCandidateWork> works)
    {
        List<ReplacementCandidateWork> told =
        [
            .. works.Select(work =>
                work.LibraryNames.Count > 0
                && works.Count(other => string.Equals(other.ConfirmationText, work.ConfirmationText, StringComparison.Ordinal)) > 1
                    ? work with { ConfirmationText = $"{work.ConfirmationText} in “{string.Join(", ", work.LibraryNames)}”" }
                    : work),
        ];
        List<List<int>> twins =
        [
            .. Enumerable.Range(0, told.Count)
                .GroupBy(at => told[at].ConfirmationText, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.ToList()),
        ];
        foreach (var group in twins)
        {
            var text = told[group[0]].ConfirmationText;
            // The copy's file name, as the planner's sentences name it: what the owner sees the copy by.
            List<string?> names =
            [
                .. group.Select(at => told[at].AllCopies.FirstOrDefault() is { } copy ? CopyEditions.FileNameOf(copy.Row.ServicePath) : null),
            ];
            var byName = names.All(name => name is not null && $"{text} (the copy “{name}”)".Length <= AuditTarget.TargetNameMaximumLength)
                && names.Distinct(StringComparer.Ordinal).Count() == names.Count;
            for (var place = 0; place < group.Count; place++)
            {
                var tail = byName
                    ? $" (the copy “{names[place]}”)"
                    : string.Create(CultureInfo.InvariantCulture, $" ({place + 1} of {group.Count})");
                told[group[place]] = told[group[place]] with { ConfirmationText = text + tail };
            }
        }

        return told;
    }

    private static List<NormalizedPath> Parsed(IEnumerable<string> paths) =>
        [.. paths.Select(text => NormalizedPath.TryParse(text, out var path, out _) ? path : null).OfType<NormalizedPath>()];

    /// <summary>The rows <see cref="LoadAsync"/> read, by what each is looked up by.</summary>
    private sealed record LibraryRows(
        ILookup<Guid, MediaEpisodePositionEntity> ClaimedPositionsBySeries,
        ILookup<Guid, MediaEpisodePositionEntity> PositionsByEpisode,
        ILookup<Guid, MediaVersionEntity> VersionsByWork,
        ILookup<Guid, PhysicalFileEntity> FilesByVersion,
        ILookup<Guid, MediaWorkTitleEntity> Titles,
        IReadOnlyDictionary<Guid, string> LocationNames,
        IReadOnlyDictionary<Guid, IReadOnlyList<PathMappingPair>> PathMappings,
        IReadOnlyDictionary<Guid, MediaInspectionEntity> Readings,
        ILookup<Guid, MediaInspectionTrackEntity> Tracks,
        IReadOnlyList<NormalizedPath> HeldPaths,
        IReadOnlyList<(long DeviceId, long Inode)> HeldIdentities);
}
