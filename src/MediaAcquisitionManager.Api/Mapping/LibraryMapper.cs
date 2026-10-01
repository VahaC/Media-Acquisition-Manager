using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="ILibraryMapper"/>
public sealed class LibraryMapper : ILibraryMapper
{
    /// <summary>The schemes a work needs to be matched reliably (specification §3.1), in the order
    /// <see cref="LibraryWorkDto.MissingIdentifiers"/> lists the ones it lacks.</summary>
    private static readonly MediaIdentifierScheme[] ReliableSchemes = [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb];

    public LibraryWorkDto ToDto(
        MediaWorkEntity work,
        IReadOnlyList<MediaWorkTitleEntity> titles,
        IReadOnlyList<MediaWorkIdentifierEntity> identifiers,
        IReadOnlyList<MediaVersionEntity> versions,
        Func<MediaVersionEntity, IReadOnlyList<LibraryFileDto>> filesOf)
    {
        // MAM-8 — chosen by the comparer the list is sorted by in SQL (the LIBRARY_TITLE collation), so
        // the title a card shows is the one that put it where it is on the page.
        var title = FirstInTitleOrder(titles, MediaTitleKind.Display);
        var originalTitle = FirstInTitleOrder(titles, MediaTitleKind.Original);
        var schemes = identifiers.Select(identifier => identifier.Scheme).ToHashSet();

        return new LibraryWorkDto(
            work.Id,
            work.Kind,
            title?.Text,
            originalTitle?.Text,
            // Unknown stays unknown: no year is made up from another source's title.
            title?.Year ?? originalTitle?.Year,
            // MAM-8 — one entry per claim (scheme, value, source): the items of one library that share their
            // ids (two files of one movie, both "Чужі") each store the id, and the card shows it once.
            [.. identifiers
                .OrderBy(identifier => identifier.Scheme)
                .ThenBy(identifier => identifier.Value, StringComparer.Ordinal)
                .ThenBy(identifier => identifier.SourceName, StringComparer.Ordinal)
                .Select(identifier => new LibraryIdentifierDto(identifier.Scheme, identifier.Value, identifier.SourceName))
                .Distinct()],
            [.. ReliableSchemes.Where(scheme => !schemes.Contains(scheme))],
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
                    [.. filesOf(version).OrderBy(file => file.ServicePath, StringComparer.Ordinal)]))]);
    }

    public LibraryFileDto ToDto(PhysicalFileEntity file, IReadOnlyList<PathMappingPair> pathMappings, string? locationName, bool forOwner)
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
            forOwner ? path.Reason : null,
            file.SizeBytes,
            locationName);
    }

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
            run.VersionsCount,
            run.FilesCount,
            // MAM-8 — the sentence can name the address or a redirect target; integrations are the
            // owner's.
            forOwner ? run.Error : null);

    public LibrarySourceDto ToDto(
        IntegrationConnectionEntity integration,
        IntegrationAdapterDescriptor? adapter,
        bool canReadLibrary,
        LibrarySyncRunDto? latestRun,
        LibrarySyncRunDto? lastSucceededRun,
        int filesWithoutAppPathCount,
        string? pathProblem) =>
        new(
            integration.Id,
            integration.Name,
            adapter?.DisplayName,
            integration.IsEnabled,
            canReadLibrary,
            latestRun,
            lastSucceededRun,
            filesWithoutAppPathCount,
            pathProblem);

    private static MediaWorkTitleEntity? FirstInTitleOrder(IReadOnlyList<MediaWorkTitleEntity> titles, MediaTitleKind kind) =>
        titles
            .Where(title => title.Kind == kind)
            .OrderBy(title => title.Text, LibraryTitleOrder.Comparer)
            .ThenBy(title => title.Text, StringComparer.Ordinal)
            .ThenBy(title => title.Id)
            .FirstOrDefault();
}
