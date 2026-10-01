using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
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
/// Two steps: the database narrows the library to the works that could match, and
/// <see cref="WorkMatching"/> — the one rule, pure and tested on its own — decides which do and on what
/// evidence. The narrowing never leaves out a work the rule would match, so the rule alone decides.
/// </para>
/// <para>
/// Never paged: a search is for one title, and the works that match it are a handful — the versions of a
/// film, a remake of another year. The page shows them all above the releases.
/// </para>
/// </remarks>
public sealed class LibraryMatcher(ApplicationDbContext db, ILibraryWorkReader reader) : ILibraryMatcher
{
    public async Task<IReadOnlyList<LibraryWorkMatch>> MatchAsync(WorkClaim query, bool forOwner, CancellationToken cancellationToken = default)
    {
        if (Candidates(query) is not { } candidates)
            return [];

        var works = await db.MediaWorks.AsNoTracking()
            .Where(work => candidates.Contains(work.Id))
            .ToListAsync(cancellationToken);
        if (works.Count == 0)
            return [];

        // Batched, no N+1: one query for the candidates' titles and one for their ids, however many.
        var workIds = works.Select(work => work.Id).ToList();
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => workIds.Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var identifiers = (await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => workIds.Contains(identifier.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(identifier => identifier.MediaWorkId);

        var matched = new List<Candidate>();
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
            if (WorkMatching.Match(query, reportedTitles, reportedIdentifiers) is not { } match)
                continue;

            // Back to the rows, for who claimed what (ADR 0006): the match names each id once, and every
            // source that claimed it is shown beside it.
            var matchedKeys = match.MatchedIdentifiers.Select(Key).ToHashSet();
            matched.Add(new Candidate(
                work,
                match,
                reportedTitles,
                reportedIdentifiers,
                [.. identifierRows.Where(identifier => matchedKeys.Contains(Key(new MediaIdentifier(identifier.Scheme, identifier.Value))))],
                match.MatchedTitle is { } matchedTitle ? titleRows[reportedTitles.IndexOf(matchedTitle)] : null));
        }

        if (matched.Count == 0)
            return [];

        var dtos = await reader.ReadAsync([.. matched.Select(candidate => candidate.Work)], forOwner, cancellationToken);
        return
        [
            .. matched
                .Select((candidate, index) => new LibraryWorkMatch(
                    dtos[index],
                    candidate.Match,
                    candidate.Titles,
                    candidate.Identifiers,
                    candidate.MatchedIdentifiers,
                    candidate.MatchedTitle))
                .OrderBy(work => work.Match.Evidence)
                // The title the card shows, as the Library page sorts by it: displayed, else original.
                .ThenBy(work => work.Work.Title ?? work.Work.OriginalTitle ?? "", LibraryTitleOrder.Comparer)
                .ThenBy(work => work.Work.Id),
        ];
    }

    /// <summary>
    /// The ids of the works that could match <paramref name="query"/>, as one query for the database; or
    /// <c>null</c> when nothing in it can match anything.
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

        var words = query.Titles
            .Select(title => TitleMatchKey.LongestWord(TitleMatchKey.For(title)))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);
        foreach (var word in words)
        {
            candidates = Either(candidates, db.MediaWorkTitles
                .Where(row => row.NormalizedText
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

    /// <summary>A work that matched, before it is read into the Library page's card.</summary>
    private sealed record Candidate(
        MediaWorkEntity Work,
        WorkMatch Match,
        IReadOnlyList<ReportedTitle> Titles,
        IReadOnlyList<MediaIdentifier> Identifiers,
        IReadOnlyList<MediaWorkIdentifierEntity> MatchedIdentifiers,
        MediaWorkTitleEntity? MatchedTitle);
}
