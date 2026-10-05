using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Core.Search;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — what the household's download jobs say about the recommended works: a work someone is downloading is shown
// as such, and one someone has dealt with — downloaded, waiting to be linked, or stopped following — isn't recommended
// any more. Read live on every list read, as the jobs move; nothing is stored.
public sealed partial class RecommendationListService
{
    /// <summary>What the download jobs say about a work: nothing, one is under way, or one has ended.</summary>
    private enum DownloadState
    {
        /// <summary>No job is of it, or only failed ones: a failed download is the same as none.</summary>
        None = 0,

        /// <summary>A job of it is followed in its client (<see cref="DownloadJobFollowing.FollowedStatuses"/>).</summary>
        Downloading = 1,

        /// <summary>A job of it was downloaded, needs linking, or was cancelled: it is left out of the list.</summary>
        Ended = 2,
    }

    /// <summary>
    /// The state of each of <paramref name="works"/> that some download job — any user's: the download serves the
    /// household, as the library does — is of; a work no job is of is absent. A job that ended counts over one under
    /// way: whatever else is still downloading, the household has dealt with the work.
    /// </summary>
    /// <remarks>
    /// A job's release is matched to a work as the Search page matches a release to the library (ADR 0012): of the
    /// work's kind — a film's releases are movies, a show's TV (ADR 0020; a film in TV/Anime or TV/Documentary is stored
    /// as a movie already) — and by an id the listing gives (a film's TMDb id, a show's TMDb TV id, an IMDb id), else by
    /// title and year as the release's title or its description claims them; never by a title alone. For a held series
    /// recommended for its missing seasons, only a release that claims one of them, or names no season — the
    /// availability check's rule: a job of a season the library has says nothing about the ones it lacks.
    /// </remarks>
    private async Task<Dictionary<Guid, DownloadState>> DownloadStatesAsync(IReadOnlyList<MatchableWork> works, CancellationToken cancellationToken)
    {
        // Every job but a failed one, which changes nothing; the release a job points at can't be removed while it
        // exists (Restrict), so the join loses none.
        var jobs = await (
                from job in db.DownloadJobs.AsNoTracking()
                where job.Status != DownloadJobStatus.Failed
                join release in db.Releases.AsNoTracking() on job.ReleaseId equals release.Id
                select new { job.Status, Release = release })
            .ToListAsync(cancellationToken);
        Dictionary<Guid, DownloadState> states = [];
        if (jobs.Count == 0 || works.Count == 0)
            return states;

        var matcher = new DownloadMatcher(works);
        foreach (var job in jobs)
        {
            var state = job.Status switch
            {
                DownloadJobStatus.Downloaded or DownloadJobStatus.NeedsLinking or DownloadJobStatus.Cancelled => DownloadState.Ended,
                _ when DownloadJobFollowing.IsFollowed(job.Status) => DownloadState.Downloading,
                // A status a later ticket adds says nothing until this rule names it.
                _ => DownloadState.None,
            };
            if (state == DownloadState.None)
                continue;
            foreach (var workId in matcher.WorksOf(job.Release))
                states[workId] = (DownloadState)Math.Max((int)states.GetValueOrDefault(workId), (int)state);
        }

        return states;
    }

    /// <summary>
    /// The recommended works indexed by their ids and title keys, so each job's release is matched against the few
    /// works it could be — <see cref="WorkMatching.Match"/> then decides — rather than against every work of the tab.
    /// </summary>
    private sealed class DownloadMatcher
    {
        private readonly Dictionary<(MediaIdentifierScheme Scheme, string Value), List<MatchableWork>> _byIdentifier = [];
        private readonly Dictionary<string, List<MatchableWork>> _byTitleKey = new(StringComparer.Ordinal);

        public DownloadMatcher(IReadOnlyList<MatchableWork> works)
        {
            foreach (var work in works)
            {
                // Keyed as WorkMatching compares: an id trimmed and upper-cased, a title by its match key.
                foreach (var identifier in work.Identifiers)
                    Add(_byIdentifier, (identifier.Scheme, Key(identifier.Value)), work);
                foreach (var key in work.Titles.Select(title => TitleMatchKey.For(title.Text)).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal))
                    Add(_byTitleKey, key, work);
            }
        }

        /// <summary>The works <paramref name="release"/> is a release of, by an id or by title and year; for a held series'
        /// missing seasons, of one of them.</summary>
        public IEnumerable<Guid> WorksOf(ReleaseEntity release)
        {
            var kind = release.Kind == ReleaseKind.Tv ? MediaWorkKind.Series : MediaWorkKind.Movie;
            var identifiers = ReleaseSearchTerms.IdentifiersOf(release);
            // The description too: Toloka's holds the Ukrainian title beside the original one, where Prowlarr's title
            // keeps one of them — the availability check's rule.
            List<ReleaseWorkClaim> claims = [Claim(kind, release.Title)];
            if (release.Description is { Length: > 0 } description)
                claims.Add(Claim(kind, description));

            var shortlist = identifiers
                .SelectMany(identifier => _byIdentifier.GetValueOrDefault((identifier.Scheme, Key(identifier.Value))) ?? [])
                .Concat(claims
                    .SelectMany(claim => claim.Titles)
                    .Select(TitleMatchKey.For)
                    .Where(key => key.Length > 0)
                    .SelectMany(key => _byTitleKey.GetValueOrDefault(key) ?? []))
                .Where(work => work.Kind == kind)
                .Distinct();
            return shortlist
                .Where(work => claims.Exists(claim =>
                    WorkMatching.Match(new WorkClaim(claim.Titles, ReleaseWorkClaims.YearsToMatch(claim), identifiers), work.Titles, work.Identifiers)?.Evidence
                        is MatchEvidence.Identifier or MatchEvidence.TitleAndYear))
                .Where(work => work.MissingSeasons.Count == 0 || ClaimsAnyOf(claims, work.MissingSeasons))
                .Select(work => work.Id);
        }

        /// <summary>Whether <paramref name="claims"/> name one of <paramref name="seasons"/>, or no season at all.</summary>
        private static bool ClaimsAnyOf(List<ReleaseWorkClaim> claims, IReadOnlySet<int> seasons)
        {
            var named = claims.Select(claim => claim.Episodes).OfType<ClaimedEpisodes>().ToList();
            return named.Count == 0
                || named.Exists(episodes => seasons.Any(season => season >= episodes.FirstSeason && season <= episodes.LastSeason));
        }

        /// <summary>What <paramref name="text"/> claims, read as a film's or as a show's release by the release's kind.</summary>
        private static ReleaseWorkClaim Claim(MediaWorkKind kind, string text) =>
            kind == MediaWorkKind.Series ? ReleaseWorkClaims.ReadTv(text) : ReleaseWorkClaims.Read(text);

        private static string Key(string value) => value.Trim().ToUpperInvariant();

        private static void Add<TKey>(Dictionary<TKey, List<MatchableWork>> index, TKey key, MatchableWork work) where TKey : notnull
        {
            if (!index.TryGetValue(key, out var listed))
                index[key] = listed = [];
            listed.Add(work);
        }
    }

    /// <summary>A recommended work as a match reads it — the availability check's reading of a catalog work.</summary>
    /// <param name="Id">The catalog work.</param>
    /// <param name="Kind">A film or a show.</param>
    /// <param name="Titles">Its catalog title, and its original title when that differs, each with its year.</param>
    /// <param name="Identifiers">Its catalog id in its kind's numbering (ADR 0017), and its IMDb id when known.</param>
    /// <param name="MissingSeasons">A held series' missing seasons; empty for any other work.</param>
    private sealed record MatchableWork(
        Guid Id,
        MediaWorkKind Kind,
        IReadOnlyList<ReportedTitle> Titles,
        IReadOnlyList<MediaIdentifier> Identifiers,
        IReadOnlySet<int> MissingSeasons)
    {
        /// <summary><paramref name="work"/>, recommended for <paramref name="missingSeasons"/> ("4,5") when it is a held
        /// series.</summary>
        /// <remarks>MAM-33 — read as the availability check reads a work (<see cref="RecommendedWorkTerms"/>).</remarks>
        public static MatchableWork Of(CatalogWorkEntity work, string? missingSeasons) =>
            new(
                work.Id,
                work.Kind,
                RecommendedWorkTerms.TitlesOf(work.Title, work.OriginalTitle, work.ReleaseDate?.Year),
                RecommendedWorkTerms.IdentifiersOf(work.Kind, work.CatalogId, work.ImdbId),
                RecommendedWorkTerms.MissingSeasonsOf(missingSeasons));
    }
}
