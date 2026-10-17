using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — what one availability pass holds in memory: the works due, each user's budget, and how a check went.
// Private to the pass: nothing of it outlives the pass but the rows it writes.
public sealed partial class RecommendationAvailabilityPass
{
    /// <summary>A recommended work whose check is due, with what its search and its matching need.</summary>
    /// <param name="CatalogWorkId">The catalog work; the check's key.</param>
    /// <param name="Tab">The tab it is recommended in: the budget it is searched from.</param>
    /// <param name="Score">Its score, the list's order.</param>
    /// <param name="NeverChecked">Whether no check of it was ever stored.</param>
    /// <param name="Kind">A film or a show.</param>
    /// <param name="CatalogId">Its catalog id, of its kind's numbering.</param>
    /// <param name="Title">Its catalog title, in the catalog's language (MAM-46).</param>
    /// <param name="OriginalTitle">Its original title, when the catalog gives one.</param>
    /// <param name="ReleaseDate">A film's release or a show's first air date.</param>
    /// <param name="ImdbId">A film's IMDb id, when its details were read.</param>
    /// <param name="VoteCount">How many rated it: the order after the score.</param>
    /// <param name="MissingSeasons">A held series' missing seasons, "4,5"; <c>null</c> for any other work.</param>
    private sealed record DueWork(
        Guid CatalogWorkId,
        RecommendationTab Tab,
        double Score,
        bool NeverChecked,
        MediaWorkKind Kind,
        string CatalogId,
        string Title,
        string? OriginalTitle,
        DateOnly? ReleaseDate,
        string? ImdbId,
        int VoteCount,
        string? MissingSeasons)
    {
        /// <summary>The title the sources are searched by (<see cref="RecommendationQueryTitle"/>).</summary>
        public string QueryText { get; } = RecommendationQueryTitle.For(Title, OriginalTitle);

        /// <summary>A film's year, which the movie search sends; a show's search sends none (ADR 0020).</summary>
        public int? QueryYear => Kind == MediaWorkKind.Movie ? ReleaseDate?.Year : null;

        /// <summary>The kind of release that can be this work: a film's are movies, a show's TV.</summary>
        public ReleaseKind ReleaseKind => Kind == MediaWorkKind.Series ? ReleaseKind.Tv : ReleaseKind.Movie;

        // MAM-33 — read as the list's download jobs read a work (RecommendedWorkTerms), so a release that counts as
        // the work here counts there too.

        /// <summary>Its titles as a match reads a work's: the catalog title, and the original one when it differs,
        /// each with its year.</summary>
        public IReadOnlyList<ReportedTitle> Titles { get; } = RecommendedWorkTerms.TitlesOf(Title, OriginalTitle, ReleaseDate?.Year);

        /// <summary>Its registry ids: its catalog id in its kind's numbering (ADR 0017), and its IMDb id when
        /// known.</summary>
        public IReadOnlyList<MediaIdentifier> Identifiers { get; } = RecommendedWorkTerms.IdentifiersOf(Kind, CatalogId, ImdbId);

        /// <summary>The missing seasons as numbers; empty for a work that isn't a held series.</summary>
        public IReadOnlySet<int> MissingSeasonNumbers { get; } = RecommendedWorkTerms.MissingSeasonsOf(MissingSeasons);
    }

    /// <summary>MAM-33 — how one work's check went, which says whether the pass goes on.</summary>
    private enum CheckOutcome
    {
        /// <summary>A source answered: what it listed is stored, and the search counted.</summary>
        Answered = 1,

        /// <summary>Every source asked failed: the search counted, nothing is stored, and the pass stops.</summary>
        Failed = 2,

        /// <summary>No source could be asked for the work's kind: no search was sent, nothing counted or stored, and
        /// the pass goes on with the other kind.</summary>
        NotAsked = 3,
    }

    /// <summary>One user's searches in the budget window and what is never due for them.</summary>
    private sealed class UserBudget(
        Guid userId,
        RecommendationSettings settings,
        Dictionary<RecommendationTab, int> usedByTab,
        int usedTotal,
        HashSet<(MediaWorkKind Kind, string CatalogId)> dismissed)
    {
        public Guid UserId { get; } = userId;

        public RecommendationSettings Settings { get; } = settings;

        /// <summary>Their searches of the window, per tab.</summary>
        public Dictionary<RecommendationTab, int> UsedByTab { get; } = usedByTab;

        /// <summary>All their searches of the window, a tab they have off since included.</summary>
        public int UsedTotal { get; private set; } = usedTotal;

        /// <summary>Whether <paramref name="work"/> is due for this user: in a tab they have on, and not one they
        /// marked "Not interested".</summary>
        public bool Wants(DueWork work) => Settings.IsOn(work.Tab) && !dismissed.Contains((work.Kind, work.CatalogId));

        /// <summary>Counts a search of <paramref name="tab"/>'s list against the budget.</summary>
        public void Count(RecommendationTab tab)
        {
            UsedByTab[tab] = UsedByTab.GetValueOrDefault(tab) + 1;
            UsedTotal++;
        }
    }
}
