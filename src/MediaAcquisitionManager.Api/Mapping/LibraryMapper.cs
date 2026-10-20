using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="ILibraryMapper"/>
/// <remarks>
/// MAM-47 (ADR 0042) — the runs' and the files' reasons are stored as key and values; they are written here, in the
/// request's language (<see cref="IRequestTexts"/>, a singleton like this mapper).
/// </remarks>
public sealed class LibraryMapper(IRequestTexts texts) : ILibraryMapper
{
    /// <summary>
    /// MAM-34 — the order in which a work's items' write states make its link's one state: the worst first, so a link one
    /// item's library lost or never took reads as such whatever the others did.
    /// </summary>
    private static readonly LibraryLinkWriteState[] WriteStatesWorstFirst =
    [
        LibraryLinkWriteState.NotWritten,
        LibraryLinkWriteState.Lost,
        LibraryLinkWriteState.Writing,
        LibraryLinkWriteState.Queued,
        LibraryLinkWriteState.Written,
        LibraryLinkWriteState.InLibrary,
        LibraryLinkWriteState.KeptInApp,
    ];

    public LibraryWorkDto ToDto(
        MediaWorkEntity work,
        IReadOnlyList<MediaWorkTitleEntity> titles,
        IReadOnlyList<MediaWorkPosterEntity> posters,
        LinkedWork linked,
        IReadOnlyList<MediaVersionEntity> versions,
        Func<MediaVersionEntity, IReadOnlyList<LibraryFileDto>> filesOf,
        IReadOnlyList<MediaEpisodePositionEntity> positions,
        IReadOnlyList<LibrarySeasonDto>? seasons,
        bool forOwner)
    {
        // MAM-8 — chosen by the comparer the list is sorted by in SQL (the LIBRARY_TITLE collation), so
        // the title a card shows is the one that put it where it is on the page.
        var title = FirstInTitleOrder(titles, MediaTitleKind.Display);
        var originalTitle = FirstInTitleOrder(titles, MediaTitleKind.Original);
        var schemes = linked.Identifiers.Select(identifier => identifier.Scheme).ToHashSet();

        return new LibraryWorkDto(
            work.Id,
            work.Kind,
            title?.Text,
            originalTitle?.Text,
            // Unknown stays unknown: no year is made up from another source's title.
            title?.Year ?? originalTitle?.Year,
            ToDtos(linked.Identifiers),
            // MAM-34 — a linked work misses nothing (decision 9), nor does an episode of a linked series (R16): the owner
            // said what it is, and a TMDb entry with no IMDb id, or a show linked to a film item, would otherwise keep it
            // "missing" for ever. LibraryService.MissingHits leaves the same works out.
            linked.IsLinked ? [] : [.. LibraryReliableSchemes.Of(work.Kind).Where(scheme => !schemes.Contains(scheme))],
            [.. versions
                .OrderBy(version => version.SourceName, LibraryTitleOrder.Comparer)
                .ThenBy(version => version.EditionLabel, LibraryTitleOrder.Comparer)
                .ThenBy(version => version.CreatedUtc)
                .ThenBy(version => version.Id)
                .Select(version => new LibraryVersionDto(
                    version.Id,
                    version.SourceName,
                    version.SourceItemId,
                    version.EditionLabel,
                    version.ObservedUtc,
                    [.. filesOf(version).OrderBy(file => file.ServicePath, StringComparer.Ordinal)]))],
            work.Kind == MediaWorkKind.Episode ? ToEpisodeDto(positions) : null,
            work.Kind == MediaWorkKind.Series ? seasons ?? [] : null,
            ToLinkDto(linked, forOwner, texts),
            PosterUrlOf(titles, posters));
    }

    // MAM-35 — the picture of the item the card is named after: the poster of the displayed title's item, else of the items
    // of the other displayed titles, then of the original titles', in title order, then any other the work has by source
    // name and item id. Only posters the route would serve count, so a card never asks for one that can only 404. An item
    // is a (connection, item id) pair: two libraries may report one id.
    private static string? PosterUrlOf(IReadOnlyList<MediaWorkTitleEntity> titles, IReadOnlyList<MediaWorkPosterEntity> posters)
    {
        var served = posters
            .Select(poster => (poster.SourceConnectionId, poster.SourceItemId, poster.SourceName, Url: LibraryPosterUrls.Of(poster.SourceItemId, poster.Tag)))
            .Where(poster => poster.Url is not null)
            .OrderBy(poster => poster.SourceName, StringComparer.Ordinal)
            .ThenBy(poster => poster.SourceItemId, StringComparer.Ordinal)
            .ThenBy(poster => poster.Url, StringComparer.Ordinal)
            .ToList();
        if (served.Count == 0)
            return null;

        var titled = InTitleOrder(titles, MediaTitleKind.Display)
            .Concat(InTitleOrder(titles, MediaTitleKind.Original))
            .Select(title => served.FindIndex(poster =>
                poster.SourceConnectionId == title.SourceConnectionId && string.Equals(poster.SourceItemId, title.SourceItemId, StringComparison.Ordinal)))
            .FirstOrDefault(index => index >= 0, 0);
        return served[titled].Url;
    }

    // MAM-34 — the work's links as one line: what the latest set links to, the worst state of all its items' links, and
    // the first conflicting library id. Who set it and why the library didn't identify it are the owner's (the sync run's
    // rule: the owner's sign-in name, and a sentence that can name the library's address). Identified and UnderWay carry no
    // secret and are for everyone: see the two below.
    private static LibraryWorkLinkDto? ToLinkDto(LinkedWork linked, bool forOwner, IRequestTexts texts)
    {
        if (linked.Links.Count == 0)
            return null;

        var latest = linked.Links[0];
        var worst = linked.Links.OrderBy(link => Array.IndexOf(WriteStatesWorstFirst, link.WriteState)).First();
        return new LibraryWorkLinkDto(
            latest.Target,
            latest.Scheme == MediaIdentifierScheme.TmdbTv ? MediaWorkKind.Series : MediaWorkKind.Movie,
            latest.Value,
            latest.CatalogTitle,
            latest.SeasonNumber,
            latest.EpisodeNumber,
            latest.ImdbId,
            forOwner ? latest.SetByUserName : null,
            latest.SetUtc,
            worst.WriteState,
            texts.Render(forOwner ? worst.WriteError : null),
            linked.ConflictingValue,
            IsIdentified(linked.Links),
            linked.Links.Any(link => link.WriteState is LibraryLinkWriteState.Queued or LibraryLinkWriteState.Writing));
    }

    // MAM-34 — whether the card may say "Identified": an item's link is Written or InLibrary, and every such link was
    // confirmed by the app (IdentifiedUtc). Without the second half, every link the earlier ids-only rule wrote would read
    // "done" right after the migration while the repair still counts it.
    private static bool IsIdentified(IReadOnlyList<LibraryItemLinkEntity> links)
    {
        var held = links.Where(link => link.WriteState is LibraryLinkWriteState.Written or LibraryLinkWriteState.InLibrary).ToList();
        return held.Count > 0 && held.All(link => link.IdentifiedUtc is not null);
    }

    // MAM-32 — the first place an episode has, by season as a series lists them (the regular ones, then the
    // specials, then none — the episodes list's order), then number (none last), then id: two sources, or
    // two items of one episode, can place it twice. MAM-20 — also the place a duplicate group's episode shows.
    internal static LibraryEpisodeDto ToEpisodeDto(IReadOnlyList<MediaEpisodePositionEntity> positions) =>
        positions
            .OrderBy(position => position.SeasonNumber is null ? 2 : position.SeasonNumber == 0 ? 1 : 0)
            .ThenBy(position => position.SeasonNumber)
            .ThenBy(position => position.EpisodeNumber ?? int.MaxValue)
            .ThenBy(position => position.SeriesWorkId is null)
            .ThenBy(position => position.Id)
            .Select(position => new LibraryEpisodeDto(position.SeriesWorkId, position.SeasonNumber, position.EpisodeNumber))
            .FirstOrDefault() ?? new LibraryEpisodeDto(null, null, null);

    public IReadOnlyList<LibraryIdentifierDto> ToDtos(IReadOnlyList<MediaWorkIdentifierEntity> identifiers) =>
        // MAM-8 — one entry per claim (scheme, value, source): the items of one library that share their
        // ids (two files of one movie, both "Чужі") each store the id, and the card shows it once.
        // MAM-34 — a link's id is a claim of its own beside the library's of the same value ("set by" and "reported by"
        // both show), and the links of two items set at once are one; ordered by the kind of source and the time too, so
        // two people's links whose names a member doesn't see keep one order.
        [.. identifiers
            .OrderBy(identifier => identifier.Scheme)
            .ThenBy(identifier => identifier.Value, StringComparer.Ordinal)
            .ThenBy(identifier => identifier.SourceName, StringComparer.Ordinal)
            .ThenBy(identifier => identifier.SourceKind)
            .ThenBy(identifier => identifier.ObservedUtc)
            .Select(identifier => new LibraryIdentifierDto(
                identifier.Scheme,
                identifier.Value,
                identifier.SourceName,
                identifier.SourceKind,
                identifier.SourceKind == ProvenanceSourceKind.Person ? identifier.ObservedUtc : null))
            .Distinct()];

    public LibraryFileDto ToDto(
        PhysicalFileEntity file,
        IReadOnlyList<PathMappingPair> pathMappings,
        string? locationName,
        MediaInspectionEntity? reading,
        IReadOnlyList<MediaInspectionTrackEntity> tracks,
        bool forOwner)
    {
        // ADR 0003 — worked out now, never stored: the integration's current mappings decide.
        var path = PathMapper.Map(file.ServicePath, pathMappings);
        return new LibraryFileDto(
            file.Id,
            file.ServicePath,
            path.AppPath,
            path.Status,
            // MAM-8 — the reason quotes the owner's mappings (an ambiguous path names both targets), the
            // same sentence the sources list keeps to the owner; everyone else still gets the status.
            texts.Render(forOwner ? path.Reason : null),
            file.SizeBytes,
            locationName,
            ToInspectionDto(file, path.HasAppPath, reading, tracks, forOwner));
    }

    public UnreportedFileDto ToDto(
        PhysicalFileEntity file,
        UnreportedFilePlaceDto place,
        MediaInspectionEntity? reading,
        IReadOnlyList<MediaInspectionTrackEntity> tracks,
        bool forOwner) =>
        new(
            file.Id,
            // MAM-18 — the scan stores the app's own path, which no mapping applies to.
            file.ServicePath,
            file.ReadSizeBytes,
            file.CreatedUtc,
            file.IgnoredUtc,
            place,
            ToInspectionDto(file, PathMapper.Map(file.ServicePath, []).HasAppPath, reading, tracks, forOwner));

    public LibraryLocationDto ToDto(LibraryLocationEntity location) => new(location.Id, location.Name, location.SourceName);

    public LibrarySyncRunDto ToDto(LibrarySyncRunEntity run, bool forOwner) =>
        new(
            run.Id,
            run.IntegrationConnectionId,
            run.IntegrationName,
            run.Status,
            // MAM-8 — only the owner starts a sync, so this is the owner's sign-in name: half of the
            // owner's credentials, and the account list is the owner's alone (UsersController).
            forOwner ? run.RequestedByUserName : null,
            run.CreatedUtc,
            run.StartedUtc,
            run.FinishedUtc,
            run.ItemsRead,
            run.ItemsTotal,
            run.WorksCount,
            run.SeriesCount,
            run.VersionsCount,
            run.FilesCount,
            // MAM-8 — the sentence can name the address or a redirect target; integrations are the
            // owner's.
            texts.Render(forOwner ? run.Error : null));

    public FileInspectionRunDto ToDto(FileInspectionRunEntity run, bool forOwner) =>
        new(
            run.Id,
            run.Trigger,
            run.Status,
            // MAM-9 — only the owner asks for one, so this is the owner's sign-in name (the sync run's
            // reason).
            forOwner ? run.RequestedByUserName : null,
            run.CreatedUtc,
            run.StartedUtc,
            run.FinishedUtc,
            run.FilesTotal,
            run.FilesChecked,
            run.FilesProbed,
            new FileHashingDto(
                run.HashCandidates,
                run.HashCandidatesChecked,
                run.HashCandidatesRead,
                run.HashProblemCount,
                // MAM-19 — the sentence may quote a path in the owner's media folders, as a file's inspection
                // problem does.
                texts.Render(forOwner ? run.HashProblem : null)),
            texts.Render(forOwner ? run.Error : null));

    public FolderScanRunDto ToDto(FolderScanRunEntity run, bool forOwner) =>
        new(
            run.Id,
            run.Status,
            // MAM-18 — only the owner asks for one: the owner's sign-in name (the inspection run's reason).
            forOwner ? run.RequestedByUserName : null,
            run.CreatedUtc,
            run.StartedUtc,
            run.FinishedUtc,
            run.FilesFound,
            run.FilesUnreported,
            run.ProblemCount,
            // The sentence quotes a path in the owner's media folders, as a file's inspection problem does.
            texts.Render(forOwner ? run.Problem : null),
            texts.Render(forOwner ? run.Error : null));

    public LibrarySourceDto ToDto(
        IntegrationConnectionEntity integration,
        IntegrationAdapterDescriptor? adapter,
        bool canReadLibrary,
        LibrarySyncRunDto? latestRun,
        LibrarySyncRunDto? lastSucceededRun,
        int filesWithoutAppPathCount,
        Message? pathProblem,
        LibrarySourceInspectionDto inspection,
        LibrarySpaceDto space) =>
        new(
            integration.Id,
            integration.Name,
            adapter?.DisplayName,
            integration.IsEnabled,
            canReadLibrary,
            latestRun,
            lastSucceededRun,
            filesWithoutAppPathCount,
            texts.Render(pathProblem),
            // MAM-47 — the reason's key, so the page can tell the first file's inspection problem is the same kind of
            // mapping problem without comparing words (LibrarySyncPanel).
            pathProblem?.Key,
            inspection,
            space);

    // MAM-9 — null until the app has looked; the facts only for a file the inspector read, so a cached
    // failure or a file that has since gone never shows the facts of other bytes. A file the last run
    // found no app path for, which the mappings give one now, is waiting for the next run rather than
    // still a problem: the owner fixed the mapping, and the page shows it at once (ADR 0003, MAM-8).
    private LibraryFileInspectionDto? ToInspectionDto(
        PhysicalFileEntity file, bool hasAppPath, MediaInspectionEntity? reading, IReadOnlyList<MediaInspectionTrackEntity> tracks, bool forOwner)
    {
        if (file is not { InspectionStatus: { } status, InspectedUtc: { } inspectedUtc })
            return null;
        if (status == FileInspectionStatus.NoAppPath && hasAppPath)
            return null;

        var media = status == FileInspectionStatus.Inspected && reading is not null
            ? new MediaFactsDto(
                reading.DurationMilliseconds,
                reading.BitRate,
                [.. tracks.OrderBy(track => track.Position).Select(track => new MediaTrackDto(
                    track.Kind,
                    track.Codec,
                    track.Profile,
                    track.Width,
                    track.Height,
                    track.DynamicRange,
                    track.Channels,
                    track.ChannelLayout,
                    track.Language,
                    track.IsDefault,
                    track.IsForced,
                    track.IsHearingImpaired))])
            : null;
        // MAM-9 — the sentence quotes paths inside the app and what the inspector said about the file:
        // the owner's diagnostics, like the path problem; everyone else reads the status.
        return new LibraryFileInspectionDto(status, texts.Render(forOwner ? file.InspectionError : null), inspectedUtc, media);
    }

    /// <summary>
    /// The title of <paramref name="kind"/> a card shows: the first in title order
    /// (<see cref="LibraryTitleOrder"/>, then ordinal, then id); <c>null</c> when the work has none.
    /// </summary>
    /// <remarks>
    /// MAM-12 — also the search page's: it orders and cuts its list by the title a card will show before
    /// reading any card (<c>LibraryMatcher</c>).
    /// </remarks>
    internal static MediaWorkTitleEntity? FirstInTitleOrder(IReadOnlyList<MediaWorkTitleEntity> titles, MediaTitleKind kind) =>
        InTitleOrder(titles, kind).FirstOrDefault();

    // The titles of kind in title order (LibraryTitleOrder, then ordinal, then id): the first is the card's.
    private static IOrderedEnumerable<MediaWorkTitleEntity> InTitleOrder(IReadOnlyList<MediaWorkTitleEntity> titles, MediaTitleKind kind) =>
        titles
            .Where(title => title.Kind == kind)
            .OrderBy(title => title.Text, LibraryTitleOrder.Comparer)
            .ThenBy(title => title.Text, StringComparer.Ordinal)
            .ThenBy(title => title.Id);
}
