using System.Text;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationFeedPass"/>
/// <remarks>
/// <para>
/// Each source is asked for the kinds some user's tabs want — films for Movies, Cartoons or Documentaries, shows for
/// Series, Cartoons or Documentaries — in one query (<see cref="IReleaseSources.LatestAsync"/>): Toloka answers its
/// latest 90 and pages nothing further (VendorSamples), which every few hours is days of releases. An entry is one
/// release of one source, kept by the source's id of it; a listing without one can't be told from the next read's,
/// and is left out.
/// </para>
/// <para>
/// An entry is resolved once, found or not: what its description claims first — Toloka's own topic title,
/// "Ukrainian / Original (year)" — then its title, each title it lists with the year it names, looked up in the
/// catalog (<see cref="ICatalogStore.LookupAsync"/>, cached 30 days) until one gives exactly one work of that title
/// and year (specification §3.1: a title and year never decide between two). A failure of the catalog stops the
/// resolving for the pass, and the entries left wait for the next one.
/// </para>
/// <para>
/// Logs carry ids, statuses and counts only, never a title or an address.
/// </para>
/// </remarks>
public sealed class RecommendationFeedPass(
    ApplicationDbContext db,
    IReleaseSources sources,
    ICatalogConnector catalogs,
    ICatalogStore store,
    IRecommendationSettingsReader settingsReader,
    IRecommendationRunService runs,
    TimeProvider time,
    ILogger<RecommendationFeedPass> logger) : IRecommendationFeedPass
{
    /// <summary>
    /// MAM-33 — the most entries one pass resolves. A source's read lists up to 90 or 100, so a pass resolves a whole
    /// read; the first passes after a catalog is connected, or after the catalog was down, work through a backlog a
    /// hundred at a time rather than in one burst of lookups.
    /// </summary>
    public const int ResolvePerPass = 100;

    /// <summary>
    /// MAM-33 — how long an entry no source lists any more is kept: 30 days, past the two weeks a release counts as
    /// new (<see cref="RecommendationRunner.FeedFreshness"/>). By when a source last listed it, so an entry still in
    /// a feed is never forgotten and found again as new.
    /// </summary>
    public static readonly TimeSpan FeedEntryLifetime = TimeSpan.FromDays(30);

    /// <summary>What a lookup title is trimmed of besides spaces: the dashes, colons, bars, commas, dots and
    /// semicolons that stood between it and what followed it.</summary>
    private const string LookupTitleEdges = "-–—:|,.;";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var users = await settingsReader.ReadEnabledUsersAsync(cancellationToken);
        var tabs = RecommendationTabs.All.Where(tab => users.Any(user => user.Settings.IsOn(tab))).ToList();
        var films = RecommendationTabs.Wants(tabs, MediaWorkKind.Movie);
        var shows = RecommendationTabs.Wants(tabs, MediaWorkKind.Series);
        if (!films && !shows)
            return;

        // Without a catalog no entry can be resolved, and no run can recommend from one: nothing is read at all.
        var connected = await catalogs.ConnectAsync(cancellationToken);
        if (connected.Connection is not { } catalog)
        {
            if (connected.Status != CatalogConnectStatus.NoCatalog)
                logger.LogInformation("The newest releases were not read: the metadata catalog can't be used ({Status}).", connected.Status);
            return;
        }

        var outcomes = await sources.LatestAsync(films, shows, cancellationToken);
        foreach (var outcome in outcomes)
            await StoreAsync(outcome, cancellationToken);

        var resolved = await ResolveAsync(catalog, films, shows, cancellationToken);
        if (resolved > 0)
            await runs.EnsureQueuedAsync(RecommendationRunTrigger.FeedRead, cancellationToken: cancellationToken);

        await PruneAsync(cancellationToken);
    }

    /// <summary>Keeps what <paramref name="outcome"/>'s source listed: a new entry for a release first seen, the rest
    /// updated and marked seen now. A source that didn't answer is logged and keeps its entries as they are.</summary>
    private async Task StoreAsync(ReleaseSourceOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.Status != IntegrationCallStatus.Succeeded)
        {
            // Information: the status only — never the sentence, which can carry the address (the connection test's rule).
            logger.LogInformation(
                "The newest releases of integration {IntegrationId} were not read: {Status}.",
                outcome.IntegrationId,
                outcome.Status?.ToString() ?? outcome.NotSearchedReason?.ToString());
            return;
        }

        // The first of a release listed twice in one answer: one entry per release of a source.
        var hits = outcome.Hits
            .Where(hit => hit.SourceItemId is not null)
            .DistinctBy(hit => hit.SourceItemId, StringComparer.Ordinal)
            .ToList();
        var ids = hits.Select(hit => hit.SourceItemId!).ToList();
        // MAM-33 — one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule), not one parameter per id: how many a
        // source lists is the source's to say.
        var existing = await db.SourceFeedEntries.AsTracking()
            .Where(entry => entry.SourceConnectionId == outcome.IntegrationId && EF.Parameter(ids).Contains(entry.SourceItemId!))
            .ToDictionaryAsync(entry => entry.SourceItemId!, StringComparer.Ordinal, cancellationToken);

        var now = time.GetUtcNow().UtcDateTime;
        var added = 0;
        foreach (var hit in hits)
        {
            if (!existing.TryGetValue(hit.SourceItemId!, out var entry))
            {
                // First seen now: the run counts a release as new from here (RecommendationRunner.FeedFreshness).
                entry = new SourceFeedEntryEntity
                {
                    SourceKind = ProvenanceSourceKind.ReleaseSearch,
                    SourceConnectionId = outcome.IntegrationId,
                    SourceItemId = hit.SourceItemId,
                    CreatedUtc = now,
                };
                db.SourceFeedEntries.Add(entry);
                added++;
            }

            // What the source says of it now; when and to what it was resolved stays.
            entry.SourceName = outcome.Name;
            entry.Title = hit.Title;
            entry.Description = hit.Description;
            entry.Kind = hit.Kind;
            entry.CategoryIds = ReleaseStore.CategoryIdsOf(hit.CategoryIds);
            entry.PublishedUtc = hit.PublishedUtc;
            entry.ObservedUtc = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // MAM-33 — the source was removed after the pass read the sources and before its entries were saved: its
            // entries went with it (Cascade), and a new one can't point at it. Nothing of it is kept, as if it had been
            // removed before the pass — and the other sources' entries still are. Any other failure is a bug.
            db.ChangeTracker.Clear();
            if (await db.IntegrationConnections.AnyAsync(integration => integration.Id == outcome.IntegrationId, cancellationToken))
                throw;
            logger.LogInformation(
                "The newest releases of integration {IntegrationId} were not stored: it was removed during the read.", outcome.IntegrationId);
            return;
        }

        db.ChangeTracker.Clear();
        logger.LogInformation(
            "The newest releases of integration {IntegrationId} listed {Count}, {Added} of them new.", outcome.IntegrationId, hits.Count, added);
    }

    /// <summary>
    /// Resolves the unresolved entries of the wanted kinds, newest first, at most <see cref="ResolvePerPass"/>; how
    /// many turned out to be a catalog work. Each entry is written as soon as it is resolved, so a failure of the
    /// catalog midway keeps what was done.
    /// </summary>
    private async Task<int> ResolveAsync(CatalogConnection catalog, bool films, bool shows, CancellationToken cancellationToken)
    {
        // A kind no tab wants is never asked of the catalog (the run's rule); its entries wait until one does.
        var entries = await db.SourceFeedEntries.AsNoTracking()
            .Where(entry => entry.ResolvedUtc == null
                && ((films && entry.Kind == ReleaseKind.Movie) || (shows && entry.Kind == ReleaseKind.Tv)))
            .OrderByDescending(entry => entry.PublishedUtc ?? entry.CreatedUtc)
            .ThenByDescending(entry => entry.CreatedUtc)
            .ThenBy(entry => entry.Id)
            .Take(ResolvePerPass)
            .Select(entry => new { entry.Id, entry.Title, entry.Description, entry.Kind })
            .ToListAsync(cancellationToken);

        var resolved = 0;
        foreach (var (position, entry) in entries.Index())
        {
            var kind = entry.Kind == ReleaseKind.Tv ? MediaWorkKind.Series : MediaWorkKind.Movie;
            var (workId, failure) = await WorkOfAsync(catalog, kind, entry.Title, entry.Description, cancellationToken);
            if (failure is { } status)
            {
                logger.LogWarning(
                    "Resolving the newest releases stopped: the metadata catalog answered {Status}; {Count} entries wait for the next pass.",
                    status,
                    entries.Count - position);
                break;
            }

            var resolvedUtc = time.GetUtcNow().UtcDateTime;
            await db.SourceFeedEntries
                .Where(row => row.Id == entry.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.ResolvedUtc, resolvedUtc)
                        .SetProperty(row => row.CatalogWorkId, workId),
                    cancellationToken);
            if (workId is not null)
                resolved++;
        }

        if (entries.Count > 0)
            logger.LogInformation("Resolved {Resolved} of {Count} feed entries to catalog works.", resolved, entries.Count);
        return resolved;
    }

    /// <summary>
    /// The catalog work an entry most likely is (<see cref="IRecommendationFeedPass"/>'s lookups), its row stored —
    /// its details read when no list stored it yet, so a run can tell its tab; <c>null</c> when nothing usable was
    /// found; or the status of a catalog failure. A work the catalog no longer has is no answer, and the lookup that
    /// named it is forgotten.
    /// </summary>
    private async Task<(Guid? WorkId, IntegrationCallStatus? Failure)> WorkOfAsync(
        CatalogConnection catalog, MediaWorkKind kind, string title, string? description, CancellationToken cancellationToken)
    {
        foreach (var lookup in LookupsOf(kind, title, description))
        {
            var answer = await store.LookupAsync(catalog, lookup, cancellationToken);
            if (!answer.IsSuccess)
                return (null, answer.Status);
            if (answer.Value! is not { IsFound: true, CatalogId: { } catalogId })
                continue;

            if (await RowOfAsync(kind, catalogId, cancellationToken) is { } known)
                return (known, null);

            var details = await store.GetWorkAsync(catalog, kind, catalogId, cancellationToken);
            if (details.Status == IntegrationCallStatus.NotFound)
            {
                await store.ForgetLookupAsync(catalog, lookup, cancellationToken);
                continue;
            }

            if (!details.IsSuccess)
                return (null, details.Status);
            if (await RowOfAsync(kind, catalogId, cancellationToken) is { } stored)
                return (stored, null);
        }

        return (null, null);
    }

    /// <summary>
    /// The lookups an entry is resolved by, in order, each once: every title the description lists with the year it
    /// names, then every title the title lists with its year — read as a film's release or a show's by the entry's
    /// kind, each title cleaned of what its reading left at its edges (<see cref="LookupTitle"/>). A claim that names
    /// no year asks nothing: a title alone never names one work.
    /// </summary>
    /// <remarks>
    /// A show's pack of later seasons, or of its specials alone, names its seasons' years, not the show's ("Yellowstone
    /// (S5) (2022-2024)" of the show begun in 2018), so it is looked up as a show of that title begun by its first year
    /// (<see cref="CatalogLookupQuery.ByTitleUpTo"/>) — the years <see cref="ReleaseWorkClaims.YearsToMatch"/> allows it,
    /// as the availability check matches it. A pack with season 1 names the year the show began, and is looked up by it.
    /// </remarks>
    private static List<CatalogLookupQuery> LookupsOf(MediaWorkKind kind, string title, string? description)
    {
        List<ReleaseWorkClaim> claims = [];
        if (description is not null)
            claims.Add(Claim(kind, description));
        claims.Add(Claim(kind, title));
        return
        [
            .. claims
                .Where(claim => claim.Year is not null)
                .SelectMany(claim => claim.Titles
                    .Select(LookupTitle)
                    .Where(text => TitleMatchKey.For(text).Length > 0)
                    .Select(text => ReleaseWorkClaims.YearsToMatch(claim) is { Earliest: null } years
                        ? CatalogLookupQuery.ByTitleUpTo(kind, text, years.Latest)
                        : CatalogLookupQuery.ByTitle(kind, text, claim.Year!.Value)))
                .DistinctBy(lookup => lookup.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// <paramref name="title"/> as the catalog is asked for it: without a bracket left open at its end and what
    /// follows it, a closing bracket nothing opened, or the punctuation and spaces at its edges.
    /// </summary>
    /// <remarks>
    /// A title a release's reading cut at a season marker inside brackets keeps the bracket: Toloka's "Ранма 1/2 (Сезон
    /// 1,2, Сезон 3: серії 01 з 12)" reads "Ранма 1/2 (" (MAM-32's reading, which other code pins, so it is cleaned
    /// here), and the catalog's search finds nothing by that. The cache key is the title's match key, which a bracket
    /// never changes. (MAM-30: "Розслідування з Андрієм Данілевичем (Сезон 1, серії 1-25 з ?)", which read so too, now
    /// reads without its bracket, as the marker takes an unknown total.)
    /// </remarks>
    private static string LookupTitle(string title)
    {
        var kept = new StringBuilder(title.Length);
        var open = new Stack<int>();
        foreach (var character in title)
        {
            if (character is '(' or '[' or '{')
            {
                open.Push(kept.Length);
                kept.Append(character);
            }
            else if (character is ')' or ']' or '}')
            {
                // One that closes nothing is dropped; one that closes a bracket is kept with it.
                if (open.Count == 0)
                    continue;
                open.Pop();
                kept.Append(character);
            }
            else
            {
                kept.Append(character);
            }
        }

        // The outermost bracket left open, and all after it: what it opened was cut away.
        var text = open.Count > 0 ? kept.ToString(0, open.Min()) : kept.ToString();
        var start = 0;
        var end = text.Length;
        while (start < end && IsLookupTitleEdge(text[start]))
            start++;
        while (end > start && IsLookupTitleEdge(text[end - 1]))
            end--;
        return text[start..end];
    }

    private static bool IsLookupTitleEdge(char character) => char.IsWhiteSpace(character) || LookupTitleEdges.Contains(character, StringComparison.Ordinal);

    private static ReleaseWorkClaim Claim(MediaWorkKind kind, string text) =>
        kind == MediaWorkKind.Series ? ReleaseWorkClaims.ReadTv(text) : ReleaseWorkClaims.Read(text);

    private Task<Guid?> RowOfAsync(MediaWorkKind kind, string catalogId, CancellationToken cancellationToken) =>
        db.CatalogWorks.AsNoTracking()
            .Where(work => work.Kind == kind && work.CatalogId == catalogId)
            .Select(work => (Guid?)work.Id)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Forgets the entries no source listed for <see cref="FeedEntryLifetime"/>.</summary>
    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - FeedEntryLifetime;
        var removed = await db.SourceFeedEntries.Where(entry => entry.ObservedUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
            logger.LogInformation("Removed {Count} feed entries no source listed for 30 days.", removed);
    }
}
