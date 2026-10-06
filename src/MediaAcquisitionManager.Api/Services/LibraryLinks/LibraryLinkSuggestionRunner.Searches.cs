using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — what a run asks the catalog: the genres and a sample of each library's identified works (the libraries'
// profiles, decision 3), then each work without a TMDb id by its titles and folder, its candidates saved one work at a
// time.
public sealed partial class LibraryLinkSuggestionRunner
{
    /// <summary>Every step that asks the catalog, in order; the first failure ends it.</summary>
    private async Task<Failure?> PrepareAsync(Preparation run, CancellationToken cancellationToken)
    {
        // The genre lists (a call or two a month, cached): a candidate's genres become roles through them.
        foreach (var kind in (MediaWorkKind[])[MediaWorkKind.Movie, MediaWorkKind.Series])
        {
            var genres = await store.GetGenresAsync(run.Catalog, kind, cancellationToken);
            if (!genres.IsSuccess)
                return Failure.Of(genres);
        }

        // By kind, then in the Library page's title order: a run the catalog stops part way has done whole kinds first.
        List<LibraryLinkPopulationWork> population = [.. (await LibraryLinkPopulation.ListAsync(db, cancellationToken)).OrderBy(work => work.Kind)];
        run.Total = population.Count;
        await WriteProgressAsync(run, cancellationToken);

        if (await WarmProfilesAsync(run, cancellationToken) is { } profileFailure)
            return profileFailure;

        List<Guid> ids = [.. population.Select(work => work.Id)];
        var titles = (await db.MediaWorkTitles.AsNoTracking().Where(title => EF.Parameter(ids).Contains(title.MediaWorkId)).ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var files = (await LibraryLinkFiles.LoadAsync(db, ids, cancellationToken)).ToLookup(file => file.WorkId);
        var roots = await db.LibraryLocationFolders.AsNoTracking().Select(folder => folder.ServicePath).ToListAsync(cancellationToken);
        var searches = await db.LibraryLinkSearches
            .AsNoTracking()
            .Where(search => EF.Parameter(ids).Contains(search.MediaWorkId))
            .ToDictionaryAsync(search => search.MediaWorkId, cancellationToken);
        var freshFrom = time.GetUtcNow().UtcDateTime - CatalogStore.RefreshAge;

        foreach (var work in population)
        {
            var plan = PlanOf(work, [.. titles[work.Id]], [.. files[work.Id]], roots);
            if (!run.BypassesCache
                && searches.GetValueOrDefault(work.Id) is { } cached
                && string.Equals(cached.QueryKey, plan.Key, StringComparison.Ordinal)
                && cached.SearchedUtc >= freshFrom)
            {
                // Searched lately by the same queries: TMDb would answer the same.
                run.Kept++;
            }
            else if (await SearchAsync(run, work, plan, cancellationToken) is { } failure)
            {
                return failure;
            }

            run.Done++;
            await AdvanceAsync(run, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Reads, through the cache, the details of up to <see cref="ProfileSampleSize"/> identified works of each library, so
    /// their genres are rows when the suggestions are read. A work the catalog no longer has is skipped.
    /// </summary>
    private async Task<Failure?> WarmProfilesAsync(Preparation run, CancellationToken cancellationToken)
    {
        var sample = (await LibraryLinkProfiles.IdentifiedAsync(db, cancellationToken))
            .SelectMany(work => work.LocationIds.Select(locationId => (LocationId: locationId, Key: new LinkCandidateKey(work.CatalogKind, work.CatalogId))))
            .GroupBy(entry => entry.LocationId)
            .SelectMany(library => library
                .Select(entry => entry.Key)
                .Distinct()
                .OrderBy(key => key.Kind)
                .ThenBy(key => key.CatalogId, StringComparer.Ordinal)
                .Take(ProfileSampleSize))
            .Distinct()
            .ToList();
        foreach (var key in sample)
        {
            var details = await store.GetWorkAsync(run.Catalog, key.Kind, key.CatalogId, cancellationToken);
            if (!details.IsSuccess && details.Status != IntegrationCallStatus.NotFound)
                return Failure.Of(details);
        }

        return null;
    }

    /// <summary>
    /// What to ask the catalog for one work: its cleaned titles and — a film or an episode — its first file's folder
    /// name, each for the kinds of catalog work it could be (a film may be an episode of a show, decision 15), with its
    /// year and without (R21: TMDb's year filter would hide a film dated a year apart). The key names all of it, so a work
    /// whose titles, folder or year change is searched again.
    /// </summary>
    private static SearchPlan PlanOf(
        LibraryLinkPopulationWork work, IReadOnlyList<MediaWorkTitleEntity> titles, IReadOnlyList<LibraryLinkFile> files, IReadOnlyList<string> roots)
    {
        var queries = new List<(string Text, LibraryLinkCandidateSource Source)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var title in titles
            .OrderBy(title => title.Kind == MediaTitleKind.Display ? 0 : 1)
            .ThenBy(title => title.Text, LibraryTitleOrder.Comparer)
            .ThenBy(title => title.Id))
        {
            var cleaned = SeedTitleCleaning.Clean(title.Text);
            if (TitleMatchKey.For(cleaned) is { Length: > 0 } titleKey && keys.Add(titleKey))
                queries.Add((cleaned, title.Kind == MediaTitleKind.Display ? LibraryLinkCandidateSource.Title : LibraryLinkCandidateSource.OriginalTitle));
        }

        // A series is named after its folder already (R23), and its episodes' folders are its seasons'.
        if (work.Kind != MediaWorkKind.Series
            && files.FirstOrDefault() is { } first
            && FolderHint.Of(first.ServicePath, roots) is { } folder
            && SeedTitleCleaning.Clean(folder) is var cleanedFolder
            && TitleMatchKey.For(cleanedFolder) is { Length: > 0 } folderKey
            && keys.Add(folderKey))
        {
            queries.Add((cleanedFolder, LibraryLinkCandidateSource.FolderName));
        }

        MediaWorkKind[] kinds = work.Kind == MediaWorkKind.Movie ? [MediaWorkKind.Movie, MediaWorkKind.Series] : [MediaWorkKind.Series];
        var year = LibraryLinkWorkTitles.YearOf(titles);
        int?[] years = year is { } known ? [known, null] : [null];
        var queryKey = FileInspectionRunUpdates.Cut(
            $"{string.Join(',', kinds)}|{year}|{string.Join('|', queries.Select(query => $"{query.Source}:{query.Text}"))}",
            LibraryLinkSearchEntity.QueryKeyMaximumLength);
        return new SearchPlan(queries, kinds, years, queryKey, LibraryLinkWorkTitles.Cleaned(titles).Select(TitleMatchKey.For).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Searches the catalog for one work as <paramref name="plan"/> says, reads each kept candidate's details
    /// through the cache, and saves them as the work's candidates.</summary>
    private async Task<Failure?> SearchAsync(Preparation run, LibraryLinkPopulationWork work, SearchPlan plan, CancellationToken cancellationToken)
    {
        var found = new List<(CatalogWorkSummary Summary, LibraryLinkCandidateSource Source)>();
        var seen = new HashSet<(MediaWorkKind, string)>();
        foreach (var (text, source) in plan.Queries)
        {
            foreach (var kind in plan.Kinds)
            {
                foreach (var year in plan.Years)
                {
                    if (!run.Answers.TryGetValue((kind, text, year), out var summaries))
                    {
                        var answer = await store.SearchAsync(run.Catalog, kind, text, year, cancellationToken);
                        if (!answer.IsSuccess)
                            return Failure.Of(answer);
                        run.Answers[(kind, text, year)] = summaries = answer.Value!;
                    }

                    found.AddRange(summaries.Where(summary => seen.Add((summary.Kind, summary.Id))).Select(summary => (summary, source)));
                }
            }
        }

        var kept = new List<(CatalogWorkSummary Summary, LibraryLinkCandidateSource Source)>();
        foreach (var candidate in LinkCandidateSelection.Choose(found, candidate => candidate.Summary, plan.TitleKeys, CandidatesPerWork))
        {
            // Through the cache: its runtime, genres, collection, directors and other titles become rows the evidence
            // reads. A film the catalog lists but no longer has is no candidate.
            var details = await store.GetWorkAsync(run.Catalog, candidate.Summary.Kind, candidate.Summary.Id, cancellationToken);
            if (details.IsSuccess)
                kept.Add(candidate);
            else if (details.Status != IntegrationCallStatus.NotFound)
                return Failure.Of(details);
        }

        if (await SaveAsync(work.Id, plan.Key, kept, cancellationToken))
            run.Searched++;
        else
            run.Gone++;
        return null;
    }

    /// <summary>
    /// Replaces the work's candidates and stamps its search, keeping the owner's "none fits"; <c>false</c> when a sync
    /// deleted the work meanwhile (R21), which is skipped.
    /// </summary>
    /// <remarks>One transaction: the dialog never reads a search half replaced. A second try once when the work's search
    /// row came in between (the owner's "none fits" on a work never searched); anything else is a bug.</remarks>
    private async Task<bool> SaveAsync(
        Guid workId, string queryKey, IReadOnlyList<(CatalogWorkSummary Summary, LibraryLinkCandidateSource Source)> candidates, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                if (!await db.MediaWorks.AnyAsync(work => work.Id == workId, cancellationToken))
                    return false;

                var now = time.GetUtcNow().UtcDateTime;
                var search = await db.LibraryLinkSearches.AsTracking().SingleOrDefaultAsync(row => row.MediaWorkId == workId, cancellationToken);
                if (search is null)
                {
                    search = new LibraryLinkSearchEntity { MediaWorkId = workId, CreatedUtc = now };
                    db.LibraryLinkSearches.Add(search);
                }
                else
                {
                    await db.LibraryLinkCandidates.Where(candidate => candidate.LibraryLinkSearchId == search.Id).ExecuteDeleteAsync(cancellationToken);
                }

                search.QueryKey = queryKey;
                search.SearchedUtc = now;
                db.LibraryLinkCandidates.AddRange(candidates.Select((candidate, position) => new LibraryLinkCandidateEntity
                {
                    LibraryLinkSearchId = search.Id,
                    Position = position,
                    CatalogKind = candidate.Summary.Kind,
                    CatalogId = candidate.Summary.Id,
                    Source = candidate.Source,
                }));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException) when (attempt == 1)
            {
                // The work deleted by a sync between the check and the save, or its search row inserted meanwhile: the
                // next attempt sees which.
                db.ChangeTracker.Clear();
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>One work's search: the queries, the kinds and years each is asked for, its cache key, and the work's own
    /// title keys (an exact title is kept first).</summary>
    private sealed record SearchPlan(
        IReadOnlyList<(string Text, LibraryLinkCandidateSource Source)> Queries,
        IReadOnlyList<MediaWorkKind> Kinds,
        IReadOnlyList<int?> Years,
        string Key,
        IReadOnlySet<string> TitleKeys);
}
