using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Duplicates;
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
/// </remarks>
public sealed partial class LibraryDuplicateService(
    ApplicationDbContext db, ILibraryDuplicateMapper mapper, ILibraryLinkReader links, IFileInventory inventory) : ILibraryDuplicateService
{
    /// <summary>How much of an unknown class a sentence repeats: the longest class is 13 characters, and a pasted page
    /// of text shouldn't come back whole (the Library's filter rule).</summary>
    private const int ClassEchoLength = 32;

    public async Task<LibraryDuplicateListResult> ListAsync(
        string? duplicateClass, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default)
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

        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        var library = await LoadAsync(forOwner, cancellationToken);

        // Only when byte-identical groups are listed: a page of another class shows no estimate to ask for.
        IReadOnlyList<long> devices = classFilter is null or DuplicateClass.ByteIdentical
            ? DuplicateGrouping.DevicesOfByteIdenticalCopies(library.Names)
            : [];
        var fileSystems = new Dictionary<long, string?>();
        if (devices.Count > 0)
        {
            foreach (var device in await inventory.DescribeFileSystemsAsync(devices, cancellationToken))
                fileSystems[device.DeviceId] = device.Type;
        }

        var groups = DuplicateGrouping.Group(library.Names, library.Works, fileSystems)
            .Where(group => classFilter is null || group.Class == classFilter)
            .Select(group => (Group: group, Title: TitleOf(group, library)))
            .OrderBy(group => group.Group.Class)
            .ThenBy(group => group.Title, LibraryTitleOrder.Comparer)
            .ThenBy(group => group.Title, StringComparer.Ordinal)
            .ThenBy(group => group.Group.Key, StringComparer.Ordinal)
            .Select(group => group.Group)
            .ToList();

        // Past the last page there is nothing to read; and a page far enough out would overflow the offset (the
        // LibraryService guard).
        var offset = (long)(page - 1) * pageSize;
        if (offset >= groups.Count)
            return LibraryDuplicateListResult.Listed(new LibraryDuplicateListResponse([], groups.Count, page, pageSize));

        var pageGroups = groups.Skip((int)offset).Take(pageSize).ToList();
        var items = await MapAsync(pageGroups, library, forOwner, cancellationToken);
        return LibraryDuplicateListResult.Listed(new LibraryDuplicateListResponse(items, groups.Count, page, pageSize));
    }

    /// <summary>
    /// The page's groups as the list shows them, with the full facts of the page's files — their readings and tracks,
    /// two queries for the page — each file by service path (ordinal), then id.
    /// </summary>
    private async Task<List<LibraryDuplicateGroupDto>> MapAsync(
        IReadOnlyList<DuplicateGroup> groups, Snapshot library, bool forOwner, CancellationToken cancellationToken)
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

        return
        [
            .. groups.Select(group =>
            {
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
                            forOwner);
                    }),
                ];
                // ADR 0018 — each file once, by the identity and size the app read, never the size a library reported.
                var space = FileSpace.Of(names.Select(name => (name.DeviceId, name.Inode, name.SizeBytes)));
                return mapper.ToDto(group, files, space);
            }),
        ];
    }

    /// <summary>
    /// What a group is ordered by: the title the Library card of its first work shows, in title order; a group of names
    /// no library reported by its first path.
    /// </summary>
    private static string TitleOf(DuplicateGroup group, Snapshot library)
    {
        if (group.WorkIds.Count == 0)
            return group.FileIds.Select(fileId => library.Files[fileId].ServicePath).Min(StringComparer.Ordinal) ?? "";

        return group.WorkIds
            .Select(workId => (LibraryMapper.FirstInTitleOrder([.. library.Titles[workId]], MediaTitleKind.Display)
                ?? LibraryMapper.FirstInTitleOrder([.. library.Titles[workId]], MediaTitleKind.Original))?.Text ?? "")
            .Order(LibraryTitleOrder.Comparer)
            .First();
    }

    private static string Echo(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength), "…");
}
