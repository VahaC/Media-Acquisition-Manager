using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <inheritdoc cref="ILibraryMatcher"/>
/// <remarks>
/// <para>
/// Two steps: the database narrows the library to the works that could be listed, and
/// <see cref="WorkMatching"/> — the one rule, pure and tested on its own — decides which match and on what
/// evidence, and which of the rest hold every word of the title. The narrowing never leaves out a work
/// either would list, so the rule alone decides.
/// </para>
/// <para>
/// The matches are never paged: a search is for one title, and the works that match it are a handful —
/// the versions of a film, a remake of another year. The page shows them all above the releases.
/// </para>
/// <para>
/// MAM-12 — the works holding the words can be many: a common word is in hundreds of titles and paths.
/// So at most <see cref="MaximumWorksByWords"/> of them are listed, the first in the list's order, and the
/// rest only counted; the cut is made by the title each card will show (<see cref="LibraryMapper"/>'s
/// pick) before any card is read, so a common word costs its candidates' titles, ids and paths, never
/// hundreds of cards.
/// </para>
/// </remarks>
public sealed class LibraryMatcher(ApplicationDbContext db, ILibraryWorkReader reader) : ILibraryMatcher
{
    /// <summary>
    /// MAM-12 — the most works a search lists by their words alone (<see cref="MatchEvidence.Words"/>,
    /// ADR 0012); the rest are counted, and the page links to the Library page searched for the text.
    /// </summary>
    public const int MaximumWorksByWords = 20;

    public async Task<LibrarySearchResult> MatchAsync(WorkClaim query, bool forOwner, CancellationToken cancellationToken = default)
    {
        if (Candidates(query) is not { } candidates)
            return LibrarySearchResult.None;

        var works = await db.MediaWorks.AsNoTracking()
            .Where(work => candidates.Contains(work.Id))
            .ToListAsync(cancellationToken);
        if (works.Count == 0)
            return LibrarySearchResult.None;

        // Batched, no N+1: one query each for the candidates' titles, their ids and their files' paths,
        // however many.
        var workIds = works.Select(work => work.Id).ToList();
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => workIds.Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var identifiers = (await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => workIds.Contains(identifier.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(identifier => identifier.MediaWorkId);
        var filePaths = (await (
                    from file in db.PhysicalFiles.AsNoTracking()
                    join version in db.MediaVersions.AsNoTracking() on file.MediaVersionId equals version.Id
                    where workIds.Contains(version.MediaWorkId)
                    select new { version.MediaWorkId, file.ServicePath })
                .ToListAsync(cancellationToken))
            .ToLookup(file => file.MediaWorkId, file => file.ServicePath);

        var listed = new List<Candidate>();
        foreach (var work in works)
        {
            // In the Library page's title order (LibraryMapper's), so of two titles that match equally the
            // one shown as matched is the one the card shows.
            var titleRows = titles[work.Id]
                .OrderBy(title => title.Text, LibraryTitleOrder.Comparer)
                .ThenBy(title => title.Text, StringComparer.Ordinal)
                .ThenBy(title => title.Id)
                .ToList();
            var identifierRows = identifiers[work.Id]
                .OrderBy(identifier => identifier.Scheme)
                .ThenBy(identifier => identifier.Value, StringComparer.Ordinal)
                .ThenBy(identifier => identifier.SourceName, StringComparer.Ordinal)
                .ThenBy(identifier => identifier.Id)
                .ToList();
            var reportedTitles = titleRows.Select(title => new ReportedTitle(title.Text, title.Kind, title.Year)).ToList();
            var reportedIdentifiers = identifierRows.Select(identifier => new MediaIdentifier(identifier.Scheme, identifier.Value)).ToList();
            // MAM-12 — a work that doesn't match may still hold every word of the title, in a title or a path.
            var match = WorkMatching.Match(query, reportedTitles, reportedIdentifiers)
                ?? WorkMatching.MatchByWords(query, reportedTitles, [.. filePaths[work.Id].Order(StringComparer.Ordinal)], reportedIdentifiers);
            if (match is null)
                continue;

            // Back to the rows, for who claimed what (ADR 0006): the match names each id once, and every
            // source that claimed it is shown beside it.
            var matchedKeys = match.MatchedIdentifiers.Select(Key).ToHashSet();
            listed.Add(new Candidate(
                work,
                match,
                reportedTitles,
                reportedIdentifiers,
                [.. identifierRows.Where(identifier => matchedKeys.Contains(Key(new MediaIdentifier(identifier.Scheme, identifier.Value))))],
                match.MatchedTitle is { } matchedTitle ? titleRows[reportedTitles.IndexOf(matchedTitle)] : null,
                // The title the card shows, as the Library page sorts by it: displayed, else original.
                (LibraryMapper.FirstInTitleOrder(titleRows, MediaTitleKind.Display)
                    ?? LibraryMapper.FirstInTitleOrder(titleRows, MediaTitleKind.Original))?.Text ?? ""));
        }

        var ordered = listed
            .OrderBy(candidate => candidate.Match.Evidence)
            .ThenBy(candidate => candidate.CardTitle, LibraryTitleOrder.Comparer)
            .ThenBy(candidate => candidate.Work.Id)
            .ToList();
        // MAM-12 — every match, then the first of the works holding the words; the rest are only counted.
        var byWords = ordered.Where(candidate => candidate.Match.Evidence == MatchEvidence.Words).ToList();
        List<Candidate> shown =
        [
            .. ordered.Where(candidate => candidate.Match.Evidence != MatchEvidence.Words),
            .. byWords.Take(MaximumWorksByWords),
        ];
        var unlistedCount = byWords.Count - Math.Min(byWords.Count, MaximumWorksByWords);
        if (shown.Count == 0)
            return LibrarySearchResult.None;

        var dtos = await reader.ReadAsync([.. shown.Select(candidate => candidate.Work)], forOwner, cancellationToken);
        return new LibrarySearchResult(
            [
                .. shown.Select((candidate, index) => new LibraryWorkMatch(
                    dtos[index],
                    candidate.Match,
                    candidate.Titles,
                    candidate.Identifiers,
                    candidate.MatchedIdentifiers,
                    candidate.MatchedTitle)),
            ],
            unlistedCount);
    }

    /// <summary>
    /// The ids of the works that could be listed for <paramref name="query"/>, as one query for the
    /// database; or <c>null</c> when nothing in it can find anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A work holding an id asked for, compared the way <see cref="WorkMatching"/> compares (trimmed, any
    /// letter case): ids are ASCII (<c>MediaIdentifierRules</c>), so SQLite's <c>upper()</c>, which folds
    /// only ASCII letters, is enough.
    /// </para>
    /// <para>
    /// Or a work with a title holding the longest word of a title asked for. A title whose key equals the
    /// query's holds every one of its words in its normalised copy once that copy's apostrophes are
    /// removed (<see cref="TitleMatchKey.LongestWord"/> says why), so this leaves out no title that
    /// matches, and the longest word leaves out the most that don't. <c>Contains</c> becomes
    /// <c>instr()</c>, which reads '%' and '_' as themselves.
    /// </para>
    /// <para>
    /// MAM-12 — or a work with a file whose path holds that word: a title or a path holding every word of
    /// the title as words (<see cref="WorkMatching.MatchByWords"/>) holds the longest one in its normalised
    /// copy for the same reason — <c>NormalizedServicePath</c> is <c>LibrarySearchText.Normalize</c> of the
    /// path, the same per-character upper-casing — so the works listed by their words are never left out
    /// either. Titles and paths are one set of texts, so both are asked the one way.
    /// </para>
    /// <para>
    /// MAM-12 — the apostrophes are removed in the query itself, nested <c>replace()</c> calls inside the
    /// one <c>instr()</c>: a key drops them (<see cref="TitleMatchKey.Apostrophes"/>), so "Ocean's Eleven"
    /// has the key "OCEANS ELEVEN" while its stored copy "OCEAN'S ELEVEN" holds no "OCEANS". One call per
    /// character of <see cref="TitleMatchKey.Apostrophes"/>, in its order;
    /// <c>SearchesLibraryMatchingHttpTests</c> stores a title with each one and finds it.
    /// </para>
    /// </remarks>
    private IQueryable<Guid>? Candidates(WorkClaim query)
    {
        IQueryable<Guid>? candidates = null;
        foreach (var identifier in query.Identifiers)
        {
            if (Key(identifier) is not { } key)
                continue;
            var (scheme, value) = key;
            candidates = Either(candidates, db.MediaWorkIdentifiers
                .Where(row => row.Scheme == scheme && row.Value.Trim().ToUpper() == value)
                .Select(row => row.MediaWorkId));
        }

        // Every text a work can be found by, with its work: each title's normalised copy, and each path of
        // a file of one of its versions.
        var texts = db.MediaWorkTitles
            .Select(title => new { title.MediaWorkId, Text = title.NormalizedText })
            .Concat(
                from file in db.PhysicalFiles
                join version in db.MediaVersions on file.MediaVersionId equals version.Id
                select new { version.MediaWorkId, Text = file.NormalizedServicePath });
        var words = query.Titles
            .Select(title => TitleMatchKey.LongestWord(TitleMatchKey.For(title)))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);
        foreach (var word in words)
        {
            candidates = Either(candidates, texts
                .Where(row => row.Text
                    .Replace("\u0027", "")
                    .Replace("\u2018", "")
                    .Replace("\u2019", "")
                    .Replace("\u02BC", "")
                    .Replace("\u02BB", "")
                    .Replace("\u0060", "")
                    .Replace("\u00B4", "")
                    .Replace("\uFF07", "")
                    .Contains(word))
                .Select(row => row.MediaWorkId));
        }

        return candidates;
    }

    private static IQueryable<Guid> Either(IQueryable<Guid>? first, IQueryable<Guid> second) =>
        first is null ? second : first.Union(second);

    /// <summary>An id the way <see cref="WorkMatching"/> compares it: trimmed and upper-cased;
    /// <c>null</c> for a blank value.</summary>
    private static (MediaIdentifierScheme Scheme, string Value)? Key(MediaIdentifier identifier)
    {
        var value = identifier.Value.Trim().ToUpperInvariant();
        return value.Length == 0 ? null : (identifier.Scheme, value);
    }

    /// <summary>A work the search lists, before it is read into the Library page's card; with the title
    /// that card will show, which the list is ordered and cut by.</summary>
    private sealed record Candidate(
        MediaWorkEntity Work,
        WorkMatch Match,
        IReadOnlyList<ReportedTitle> Titles,
        IReadOnlyList<MediaIdentifier> Identifiers,
        IReadOnlyList<MediaWorkIdentifierEntity> MatchedIdentifiers,
        MediaWorkTitleEntity? MatchedTitle,
        string CardTitle);
}
