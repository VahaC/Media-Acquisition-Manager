using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Search;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — the library as a run sees it: read once into memory, in a few queries, and asked "does it hold this
// catalog work" for every candidate. Read only: nothing here writes to a library table (ADR 0025).
public sealed partial class RecommendationRunner
{
    /// <summary>
    /// The library's films and series — a series only while it holds an episode, as on the Library page — with
    /// their titles, ids and held seasons, and what the names of the files only a folder scan found claim. Five
    /// queries, however large the library.
    /// </summary>
    private async Task<LibraryContents> ReadLibraryAsync(CancellationToken cancellationToken)
    {
        var works = await db.MediaWorks.AsNoTracking()
            .Where(work => work.Kind == MediaWorkKind.Movie
                || (work.Kind == MediaWorkKind.Series && db.MediaEpisodePositions.Any(position => position.SeriesWorkId == work.Id)))
            .Select(work => new { work.Id, work.Kind })
            .ToListAsync(cancellationToken);
        var workIds = works.Select(work => work.Id).ToList();
        // MAM-33 — the ids as one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule), not one parameter per id:
        // a large library's works would run into SQLite's limit on the number of parameters.
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => EF.Parameter(workIds).Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var identifiers = (await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => EF.Parameter(workIds).Contains(identifier.MediaWorkId))
                .Select(identifier => new { identifier.MediaWorkId, identifier.Scheme, identifier.Value })
                .ToListAsync(cancellationToken))
            .ToLookup(identifier => identifier.MediaWorkId, identifier => new MediaIdentifier(identifier.Scheme, identifier.Value));
        // MAM-32 — a season is held once one of its episodes is: a partly held season isn't missing. The specials
        // (season 0) and episodes of no season say nothing of the regular seasons.
        var seasons = (await db.MediaEpisodePositions.AsNoTracking()
                .Where(position => position.SeriesWorkId != null && position.SeasonNumber > 0)
                .Select(position => new { SeriesWorkId = position.SeriesWorkId!.Value, SeasonNumber = position.SeasonNumber!.Value })
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToLookup(position => position.SeriesWorkId, position => position.SeasonNumber);
        // MAM-18, ADR 0021 — the files the scan found that no library reported. Their names never say what a file is;
        // here they only keep a work out of the lists, as the owner evidently has it.
        var scanned = await db.PhysicalFiles.AsNoTracking()
            .Where(file => file.SourceKind == ProvenanceSourceKind.FileInspection && file.MediaVersionId == null)
            .Select(file => file.ServicePath)
            .ToListAsync(cancellationToken);

        var library = new LibraryContents(
            [
                .. works.Select(work => new LibraryWork(
                    work.Id,
                    work.Kind,
                    [.. titles[work.Id].Select(title => new ReportedTitle(title.Text, title.Kind, title.Year))],
                    [.. identifiers[work.Id]],
                    DisplayTitleOf([.. titles[work.Id]]),
                    seasons[work.Id].ToHashSet())),
            ],
            [.. scanned.SelectMany(FileClaims)]);

        foreach (var work in library.Works)
        {
            foreach (var identifier in work.Identifiers)
            {
                var value = identifier.Value.Trim();
                if (CatalogScheme(work.Kind) == identifier.Scheme && value.Length > 0)
                    library.CatalogIds.Add((work.Kind, value));
                else if (identifier.Scheme == MediaIdentifierScheme.Imdb && value.Length > 0)
                    library.ImdbIds.Add((work.Kind, value.ToUpperInvariant()));
            }

            foreach (var key in work.Titles.Select(title => TitleMatchKey.For(title.Text)).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal))
            {
                if (!library.ByTitleKey.TryGetValue((work.Kind, key), out var sharing))
                    library.ByTitleKey[(work.Kind, key)] = sharing = [];
                sharing.Add(work);
            }
        }

        return library;
    }

    /// <summary>
    /// Whether the library holds <paramref name="work"/> (ADR 0012's rules, kinds kept apart: a film is matched
    /// against the library's films, a show against its series): by its catalog id — a library work's own, or a
    /// seed's, a possible match's included; by its IMDb id when known; by title and year
    /// (<see cref="WorkMatching.Match"/> with evidence Identifier or TitleAndYear); or by the name of a file only a
    /// folder scan found, by title and year.
    /// </summary>
    private static bool IsHeld(Computation run, CatalogWorkSummary work, string? imdbId)
    {
        var library = run.Library;
        if (library.CatalogIds.Contains((work.Kind, work.Id)))
            return true;
        if (imdbId is not null && library.ImdbIds.Contains((work.Kind, imdbId.Trim().ToUpperInvariant())))
            return true;

        var year = work.ReleaseDate?.Year;
        List<ReportedTitle> titles = [new ReportedTitle(work.Title, MediaTitleKind.Display, year)];
        if (work.OriginalTitle is { } original && !string.Equals(original, work.Title, StringComparison.Ordinal))
            titles.Add(new ReportedTitle(original, MediaTitleKind.Original, year));
        List<MediaIdentifier> identifiers = [new MediaIdentifier(CatalogScheme(work.Kind), work.Id)];
        if (imdbId is not null)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId));
        var claim = new WorkClaim([.. titles.Select(title => title.Text)], ClaimedYears.Exactly(year), identifiers);

        var keys = titles.Select(title => TitleMatchKey.For(title.Text)).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in keys)
        {
            foreach (var held in library.ByTitleKey.GetValueOrDefault((work.Kind, key)) ?? [])
            {
                if (WorkMatching.Match(claim, held.Titles, held.Identifiers)?.Evidence is MatchEvidence.Identifier or MatchEvidence.TitleAndYear)
                    return true;
            }

            foreach (var fileClaim in library.FileClaimsByTitleKey.GetValueOrDefault(key) ?? [])
            {
                if (WorkMatching.Match(fileClaim, titles, [])?.Evidence is MatchEvidence.TitleAndYear)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What a scan-found file's name claims (<see cref="ReleaseWorkClaims.Read"/>): its file name without the
    /// extension, then its folder's name — "Aliens (1986)/aliens.mkv" names its work by the folder. A name that
    /// names no year claims nothing.
    /// </summary>
    private static IEnumerable<WorkClaim> FileClaims(string path)
    {
        var parts = path.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            yield break;

        foreach (var name in new[] { Path.GetFileNameWithoutExtension(parts[^1]), parts.Length > 1 ? parts[^2] : null })
        {
            if (name is null)
                continue;
            var read = ReleaseWorkClaims.Read(name);
            if (read.Titles.Count > 0 && read.Year is not null)
                yield return new WorkClaim(read.Titles, ClaimedYears.Exactly(read.Year), []);
        }
    }

    /// <summary>The title a work's Library card shows (<c>LibraryMapper</c>'s pick): a displayed title, else an
    /// original one; <c>""</c> for a work with neither.</summary>
    private static string DisplayTitleOf(IReadOnlyList<MediaWorkTitleEntity> titles) =>
        (LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display) ?? LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original))?.Text ?? "";

    /// <summary>The registry the catalog's ids of a kind are in: TMDb numbers films and shows apart (ADR 0017).</summary>
    private static MediaIdentifierScheme CatalogScheme(MediaWorkKind kind) =>
        kind == MediaWorkKind.Series ? MediaIdentifierScheme.TmdbTv : MediaIdentifierScheme.Tmdb;
}
