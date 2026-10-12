using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.CopyRemovals;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <inheritdoc cref="ILibraryDuplicateService"/>
/// <remarks>
/// <para>
/// MAM-20, ADR 0028 — loading and mapping only: which names are a group, and why, is <see cref="DuplicateGrouping"/>'s,
/// pure and in Core. The whole library is read for every request (a fixed handful of queries, <see cref="LoadAsync"/>),
/// as a group can join any two names of it; the page's own files' facts are read after, for the page alone.
/// </para>
/// <para>
/// The file inventory is asked only for the file system of the devices of byte-identical files, and not at all when
/// there are none or another class is asked for: it decides whether removing a copy frees its room (Vasyl,
/// 2026-10-07). No download client is injected, by design (ADR 0018).
/// </para>
/// <para>
/// MAM-40 — each file of a page's exact copies and one work's copies carries the verdict on removing it by what is stored
/// (<see cref="CopyRemovalRules"/>, through <see cref="ICopyRemovalVerdicts"/>, read once per page); the torrents are the
/// Remove request's to read, never the list's. A name in the quarantine counts in no group (<see cref="LoadAsync"/>).
/// </para>
/// </remarks>
public sealed partial class LibraryDuplicateService(
    ApplicationDbContext db, ILibraryDuplicateMapper mapper, ILibraryLinkReader links, IFileInventory inventory, ICopyRemovalVerdicts removals)
    : ILibraryDuplicateService
{
    /// <summary>How much of an unknown class a sentence repeats: the longest class is 13 characters, and a pasted page
    /// of text shouldn't come back whole (the Library's filter rule).</summary>
    private const int ClassEchoLength = 32;

    public async Task<LibraryDuplicateListResult> ListAsync(
        string? duplicateClass, string? decision, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return LibraryDuplicateListResult.Invalid(pagingError);
        DuplicateClass? classFilter = null;
        if (!string.IsNullOrWhiteSpace(duplicateClass))
        {
            // By name, never by number: Enum.TryParse would take "2" for ByteIdentical.
            var name = Enum.GetNames<DuplicateClass>().FirstOrDefault(candidate => candidate.Equals(duplicateClass.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return LibraryDuplicateListResult.Invalid(
                    $"“{Echo(duplicateClass.Trim(), ClassEchoLength)}” is not a duplicate class. Use SameFile, ByteIdentical, SameWork or Possible.");
            }

            classFilter = Enum.Parse<DuplicateClass>(name);
        }

        if (DuplicateDecisionRules.ValidateFilter(decision, out var decisionFilter) is { } decisionError)
            return LibraryDuplicateListResult.Invalid(decisionError);

        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        var library = await LoadAsync(forOwner, cancellationToken);
        // MAM-21 — the owner's decisions, read whole (a row per group decided, a small table: one query) and met with the
        // groups by key. A key carries its class, so a row only ever meets the group it was given for; a row whose key no
        // group has now — its members changed — matches nothing and stays (ADR 0029).
        var decisions = await db.DuplicateMatchDecisions.AsNoTracking()
            .ToDictionaryAsync(row => row.GroupKey, StringComparer.Ordinal, cancellationToken);

        // Only when byte-identical groups can be listed: a page of another class shows no estimate to ask for, nor does a
        // page of confirmed matches (MAM-21), as only a possible match is ever confirmed. MAM-40 — a page of dismissed
        // groups can list byte-identical copies kept as they are, whose estimate is as sure as on their own tab.
        IReadOnlyList<long> devices = classFilter is null or DuplicateClass.ByteIdentical && decisionFilter is null or DuplicateMatchDecision.Dismissed
            ? DuplicateGrouping.DevicesOfByteIdenticalCopies(library.Names)
            : [];
        var fileSystems = new Dictionary<long, string?>();
        if (devices.Count > 0)
        {
            foreach (var device in await inventory.DescribeFileSystemsAsync(devices, cancellationToken))
                fileSystems[device.DeviceId] = device.Type;
        }

        var allGroups = DuplicateGrouping.Group(library.Names, library.Works, fileSystems);
        // MAM-40 — the decisions that hold for the groups as they are now (DuplicateDecisionRules.IsInForce): kept copies a
        // new file has joined are undecided again, on their tabs and without a decision on their card; their row is
        // ignored, never deleted.
        var inForce = allGroups
            .Where(group => decisions.TryGetValue(group.Key, out var row) && DuplicateDecisionRules.IsInForce(group, row.KeptMembers))
            .ToDictionary(group => group.Key, group => decisions[group.Key], StringComparer.Ordinal);
        var groups = allGroups
            .Where(group => classFilter is null || group.Class == classFilter)
            // MAM-21 (Vasyl, 2026-10-07) — a dismissed possible match leaves the list, every class's tab included; the
            // dismissed ones are listed apart, where the decision can be undone. MAM-40 — so do copies kept as they are.
            .Where(group => decisionFilter is null
                ? inForce.GetValueOrDefault(group.Key)?.Decision != DuplicateMatchDecision.Dismissed
                : inForce.GetValueOrDefault(group.Key)?.Decision == decisionFilter)
            .Select(group => (Group: group, Order: OrderOf(group, library)))
            .OrderBy(group => group.Group.Class)
            .ThenBy(group => group.Order.Title, LibraryTitleOrder.Comparer)
            .ThenBy(group => group.Order.Title, StringComparer.Ordinal)
            .ThenBy(group => group.Order.SeasonRank)
            .ThenBy(group => group.Order.Season)
            .ThenBy(group => group.Order.Episode)
            .ThenBy(group => group.Group.Key, StringComparer.Ordinal)
            .Select(group => group.Group)
            .ToList();

        // Past the last page there is nothing to read; and a page far enough out would overflow the offset (the
        // LibraryService guard).
        var offset = (long)(page - 1) * pageSize;
        if (offset >= groups.Count)
            return LibraryDuplicateListResult.Listed(new LibraryDuplicateListResponse([], groups.Count, page, pageSize));

        var pageGroups = groups.Skip((int)offset).Take(pageSize).ToList();
        var items = await MapAsync(pageGroups, library, inForce, forOwner, cancellationToken);
        return LibraryDuplicateListResult.Listed(new LibraryDuplicateListResponse(items, groups.Count, page, pageSize));
    }

    public async Task<DuplicateGroup?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        // As the list reads the library, for a caller who isn't the owner: the owner-only names change no key.
        var library = await LoadAsync(forOwner: false, cancellationToken);
        return DuplicateGrouping.Group(library.Names, library.Works, new Dictionary<long, string?>())
            .FirstOrDefault(group => group.Key.Equals(key, StringComparison.Ordinal));
    }

    /// <summary>
    /// The page's groups as the list shows them, with the full facts of the page's files — their readings and tracks,
    /// two queries for the page — each file by service path (ordinal), then id; MAM-21 — and the owner's decision on each
    /// from <paramref name="decisions"/>, by key.
    /// </summary>
    private async Task<List<LibraryDuplicateGroupDto>> MapAsync(
        IReadOnlyList<DuplicateGroup> groups,
        Snapshot library,
        IReadOnlyDictionary<string, DuplicateMatchDecisionEntity> decisions,
        bool forOwner,
        CancellationToken cancellationToken)
    {
        var namesById = library.Names.ToDictionary(name => name.FileId);
        var readingIds = groups
            .SelectMany(group => group.FileIds)
            .Distinct()
            .Select(fileId => library.Files[fileId].MediaInspectionId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var readings = await db.MediaInspections.AsNoTracking()
            .Where(reading => EF.Parameter(readingIds).Contains(reading.Id))
            .ToDictionaryAsync(reading => reading.Id, cancellationToken);
        var tracks = (await db.MediaInspectionTracks.AsNoTracking()
                .Where(track => EF.Parameter(readingIds).Contains(track.MediaInspectionId))
                .ToListAsync(cancellationToken))
            .ToLookup(track => track.MediaInspectionId);
        // MAM-40 — the gates and the holds once for the page, and only when it has a group a copy can be removed from.
        var judge = groups.Any(group => group.Class is DuplicateClass.SameWork or DuplicateClass.ByteIdentical)
            ? await removals.ReadAsync(cancellationToken)
            : null;

        return
        [
            .. groups.Select(group =>
            {
                var verdicts = VerdictsOf(group, library, namesById, judge);
                List<DuplicateName> names =
                [
                    .. group.FileIds
                        .Select(fileId => namesById[fileId])
                        .OrderBy(name => library.Files[name.FileId].ServicePath, StringComparer.Ordinal)
                        .ThenBy(name => name.FileId),
                ];
                List<LibraryDuplicateFileDto> files =
                [
                    .. names.Select(name =>
                    {
                        var file = library.Files[name.FileId];
                        // MAM-40 — the verdict added after mapping, as a computed field is.
                        return mapper.ToDto(
                            name,
                            file,
                            MappingsOf(file, library.PathMappings),
                            file.LibraryLocationId is { } locationId ? library.LocationNames.GetValueOrDefault(locationId) : null,
                            file.MediaInspectionId is { } readingId ? readings.GetValueOrDefault(readingId) : null,
                            file.MediaInspectionId is { } trackReadingId ? [.. tracks[trackReadingId]] : [],
                            [.. name.Holders.Select(holder => mapper.ToDto(
                                library.WorkRows[holder.WorkId],
                                [.. library.Titles[holder.WorkId]],
                                [.. library.Positions[holder.WorkId]],
                                seriesId => [.. library.Titles[seriesId]],
                                library.Versions[holder.VersionId],
                                library.Linked[holder.WorkId]))],
                            forOwner) with { Removal = verdicts.GetValueOrDefault(name.FileId) };
                    }),
                ];
                // ADR 0018 — each file once, by the identity and size the app read, never the size a library reported.
                var space = FileSpace.Of(names.Select(name => (name.DeviceId, name.Inode, name.SizeBytes)));
                return mapper.ToDto(
                    group, files, space, decisions.GetValueOrDefault(group.Key) is { } decision ? mapper.ToDto(decision, forOwner) : null);
            }),
        ];
    }

    /// <summary>
    /// What a group is ordered by: the title the Library card of its first work shows, in title order; a group of names
    /// no library reported by its first path.
    /// </summary>
    /// <remarks>
    /// MAM-21 — an episode by what its group's card is headed by: its series' title, then its season (specials after the
    /// numbered seasons, a place without one last, as the Library lists them) and its number. By the episode's own title,
    /// one show's episode groups came in no order the page shows and two shows' came between each other.
    /// </remarks>
    private static (string Title, int SeasonRank, int Season, int Episode) OrderOf(DuplicateGroup group, Snapshot library)
    {
        if (group.WorkIds.Count == 0)
            return (group.FileIds.Select(fileId => library.Files[fileId].ServicePath).Min(StringComparer.Ordinal) ?? "", 0, 0, 0);

        return group.WorkIds
            .Select(workId => OrderOf(workId, library))
            .OrderBy(order => order.Title, LibraryTitleOrder.Comparer)
            .ThenBy(order => order.Title, StringComparer.Ordinal)
            .ThenBy(order => order.SeasonRank)
            .ThenBy(order => order.Season)
            .ThenBy(order => order.Episode)
            .First();
    }

    /// <summary>
    /// A work's place in the list: its card's title; an episode of a series its series' card title with the place its
    /// card shows (<see cref="LibraryMapper.ToEpisodeDto"/>, as <see cref="LibraryDuplicateMapper"/> heads it).
    /// </summary>
    private static (string Title, int SeasonRank, int Season, int Episode) OrderOf(Guid workId, Snapshot library)
    {
        var title = CardTitleOf([.. library.Titles[workId]]) ?? "";
        if (library.WorkRows[workId].Kind != MediaWorkKind.Episode)
            return (title, 0, 0, 0);

        var place = LibraryMapper.ToEpisodeDto([.. library.Positions[workId]]);
        var seriesTitle = place.SeriesId is { } seriesId ? CardTitleOf([.. library.Titles[seriesId]]) : null;
        return (
            seriesTitle ?? title,
            place.SeasonNumber is null ? 2 : place.SeasonNumber == 0 ? 1 : 0,
            place.SeasonNumber ?? 0,
            place.EpisodeNumber ?? int.MaxValue);
    }

    /// <summary>A card's title: a displayed one, else an original one (MAM-8).</summary>
    private static string? CardTitleOf(IReadOnlyList<MediaWorkTitleEntity> titles) =>
        (LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display)
            ?? LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original))?.Text;

    private static string Echo(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength), "…");
}
