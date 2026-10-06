using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — what one run holds in memory while it computes: the library as read, the seeds, the candidates with
// their reasons. Private to the runner: nothing of it outlives the run but the rows it stores.
public sealed partial class RecommendationRunner
{
    /// <summary>One run's state: what it asks the catalog for, and everything it has collected.</summary>
    private sealed class Computation(Guid runId, CatalogConnection catalog, IReadOnlyList<RecommendationTab> tabs, DateOnly today)
    {
        public CatalogConnection Catalog { get; } = catalog;

        /// <summary>The stage it is in, as the page reads it off the run's row.</summary>
        public StageProgress Progress { get; } = new(runId);

        /// <summary>The tabs some user has on.</summary>
        public IReadOnlyList<RecommendationTab> Tabs { get; } = tabs;

        /// <summary>Today, by the clock, in UTC: a work released after it, or of no date, is left out everywhere.</summary>
        public DateOnly Today { get; } = today;

        /// <summary>The kinds of work the tabs want: a kind no tab holds is never asked of the catalog.</summary>
        public IReadOnlyList<MediaWorkKind> Kinds { get; } =
            [.. new[] { MediaWorkKind.Movie, MediaWorkKind.Series }.Where(kind => RecommendationTabs.Wants(tabs, kind))];

        /// <summary>Every genre's role, by kind and catalog id.</summary>
        public Dictionary<(MediaWorkKind Kind, int GenreId), CatalogGenreRole> GenreRoles { get; } = [];

        public LibraryContents Library { get; set; } = null!;

        /// <summary>The library's catalog works, by kind and catalog id.</summary>
        public Dictionary<(MediaWorkKind Kind, string CatalogId), Seed> Seeds { get; } = [];

        /// <summary>Every work a signal named, by kind and catalog id, with its reasons so far.</summary>
        public Dictionary<(MediaWorkKind Kind, string CatalogId), Candidate> Candidates { get; } = [];

        public bool Wants(MediaWorkKind kind) => Kinds.Contains(kind);

        public bool IsReleased(CatalogWorkSummary work) => work.ReleaseDate is { } date && date <= Today;

        /// <summary>The tab a work's genres put it in; a genre the lists don't name decides nothing.</summary>
        public RecommendationTab TabOf(CatalogWorkSummary work) =>
            RecommendationTabs.Classify(
                work.Kind, work.GenreIds.Select(id => GenreRoles.GetValueOrDefault((work.Kind, id), CatalogGenreRole.Other)));

        /// <summary>The catalog ids of the genres of <paramref name="role"/> in <paramref name="kind"/>'s list.</summary>
        public IReadOnlyList<int> GenresOf(MediaWorkKind kind, CatalogGenreRole role) =>
            [.. GenreRoles.Where(genre => genre.Key.Kind == kind && genre.Value == role).Select(genre => genre.Key.GenreId).Order()];

        /// <summary>The candidate of <paramref name="work"/>, added on first mention; the latest summary stands.</summary>
        public Candidate CandidateFor(CatalogWorkSummary work)
        {
            if (!Candidates.TryGetValue((work.Kind, work.Id), out var candidate))
                Candidates[(work.Kind, work.Id)] = candidate = new Candidate(work);
            return candidate;
        }
    }

    /// <summary>A library work as the run reads it: a film, or a series holding at least one episode — MAM-34: or an
    /// episode an episode-to-show link names, which only seeds its show.</summary>
    /// <param name="Id">The library work.</param>
    /// <param name="Kind">Movie or Series; Episode for an episode only a link names.</param>
    /// <param name="Titles">Every title any source gave it, with its year.</param>
    /// <param name="Identifiers">Every registry id any source gave it — MAM-34: with its items' TMDb links applied, as
    /// the Library page counts them.</param>
    /// <param name="DisplayTitle">The title its Library card shows, which a "Recommended" reason names.</param>
    /// <param name="HeldSeasons">A series' regular seasons with at least one episode in the library.</param>
    /// <param name="IsLinked">MAM-34 — one of its items has a TMDb link: the owner said what it is, so the run never
    /// guesses (R15).</param>
    /// <param name="HasConflict">MAM-34 — one of its linked items holds a library id that disagrees with its link (a
    /// third TMDb id beside a film link, a film id beside an episode's link to its show): neither wins, so it seeds
    /// nothing, while its ids still count as held.</param>
    private sealed record LibraryWork(
        Guid Id,
        MediaWorkKind Kind,
        IReadOnlyList<ReportedTitle> Titles,
        IReadOnlyList<MediaIdentifier> Identifiers,
        string DisplayTitle,
        IReadOnlySet<int> HeldSeasons,
        bool IsLinked,
        bool HasConflict);

    /// <summary>MAM-34 — an episode-to-show link: the library work it sits on is an episode of the show.</summary>
    /// <param name="Work">The library work: a film item the library lists as a film, or an episode of no series.</param>
    /// <param name="ShowId">The show's TMDb id.</param>
    /// <param name="ImdbId">The show's IMDb id TMDb gave; <c>null</c> when it gave none.</param>
    /// <param name="SeasonNumber">The season it names; <c>null</c> when it names none.</param>
    private sealed record ShowLink(LibraryWork Work, string ShowId, string? ImdbId, int? SeasonNumber);

    /// <summary>The library in memory, indexed for "does it hold this work".</summary>
    private sealed class LibraryContents(IReadOnlyList<LibraryWork> works, IReadOnlyList<ShowLink> showLinks, IReadOnlyList<WorkClaim> fileClaims)
    {
        public IReadOnlyList<LibraryWork> Works { get; } = works;

        /// <summary>MAM-34 — the episode-to-show links: their shows are held, and seed.</summary>
        public IReadOnlyList<ShowLink> ShowLinks { get; } = showLinks;

        /// <summary>The catalog works the library holds by id: its works' own TMDb ids (MAM-34: their links' included, and
        /// the shows the episode-to-show links name), then every seed's — a possible match's included (Vasyl's
        /// ticket).</summary>
        public HashSet<(MediaWorkKind Kind, string CatalogId)> CatalogIds { get; } = [];

        /// <summary>Its works' IMDb ids by kind, upper-cased.</summary>
        public HashSet<(MediaWorkKind Kind, string ImdbId)> ImdbIds { get; } = [];

        /// <summary>Its works by kind and title match key, so a work is matched only against those sharing a title.</summary>
        public Dictionary<(MediaWorkKind Kind, string Key), List<LibraryWork>> ByTitleKey { get; } = [];

        /// <summary>What the names of the files a folder scan found that no library reported claim, by title match key
        /// (ADR 0021: for exclusion only — never what a file is).</summary>
        public Dictionary<string, List<WorkClaim>> FileClaimsByTitleKey { get; } = Index(fileClaims);

        private static Dictionary<string, List<WorkClaim>> Index(IReadOnlyList<WorkClaim> claims)
        {
            var index = new Dictionary<string, List<WorkClaim>>(StringComparer.Ordinal);
            foreach (var claim in claims)
            {
                foreach (var key in claim.Titles.Select(TitleMatchKey.For).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal))
                {
                    if (!index.TryGetValue(key, out var list))
                        index[key] = list = [];
                    list.Add(claim);
                }
            }

            return index;
        }
    }

    /// <summary>A catalog work the library holds, with the library works it stands for.</summary>
    private sealed class Seed(CatalogWork details)
    {
        public CatalogWork Details { get; } = details;

        public CatalogWorkSummary Summary => Details.Summary;

        public List<LibraryWork> Works { get; } = [];

        /// <summary>MAM-34 — the seasons the episode-to-show links among its works name, a link naming none as
        /// <c>null</c>.</summary>
        public List<int?> LinkedSeasons { get; } = [];
    }

    /// <summary>A work some signal named, with what it collected.</summary>
    private sealed class Candidate(CatalogWorkSummary summary)
    {
        public CatalogWorkSummary Summary { get; } = summary;

        public List<Reason> Reasons { get; } = [];

        /// <summary>The seeds whose details recommend it.</summary>
        public HashSet<(MediaWorkKind Kind, string CatalogId)> Recommenders { get; } = [];

        /// <summary>A held series' missing seasons, "4,5"; <c>null</c> otherwise.</summary>
        public string? MissingSeasons { get; set; }
    }

    /// <summary>One reason, as its row stores it.</summary>
    private sealed record Reason(
        RecommendationReasonKind Kind,
        string? Name = null,
        int? Held = null,
        int? Total = null,
        string? Titles = null,
        string? Seasons = null,
        DateTime? DateUtc = null);
}
