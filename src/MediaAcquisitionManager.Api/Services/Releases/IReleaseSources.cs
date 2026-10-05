using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>
/// Asks every enabled release source for one title (MAM-10). The one place the sources are asked, shared
/// by the plain release search and the stored search (MAM-12), so both ask the same way: concurrently,
/// each answering for itself, every secret masked out of what it says. MAM-33 — and by the recommendations'
/// availability check (<see cref="SearchKindAsync"/>) and their feed of newest releases
/// (<see cref="LatestAsync"/>), through the same machinery.
/// </summary>
public interface IReleaseSources
{
    /// <summary>
    /// Asks each enabled release source, in name order, concurrently, by the queries <c>ReleaseSearchPlanning</c>
    /// makes from what that source can search by; one outcome per source, in that order. Never throws but
    /// for <paramref name="cancellationToken"/>: a source that fails, or that the app can't ask, answers
    /// with its own status and masked sentence while the others still list theirs. Nothing is stored.
    /// MAM-32 — each source is asked for movies, then for TV, each until a tier lists a release, and lists
    /// the movies then the TV releases, a listing both find once; one query that fails fails the source.
    /// With a <paramref name="season"/>, for TV alone.
    /// </summary>
    /// <param name="text">The title, already validated and trimmed (<see cref="ReleaseSearchTerms"/>).</param>
    /// <param name="year">The year; <c>null</c> for none.</param>
    /// <param name="season">The season (MAM-32); <c>null</c> for none.</param>
    /// <param name="identifiers">The ids asked for, IMDb before TMDb.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    Task<IReadOnlyList<ReleaseSourceOutcome>> SearchAsync(
        string text,
        int? year,
        int? season,
        IReadOnlyList<MediaIdentifier> identifiers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-33 — asks each enabled release source for one work whose kind is known, as <see cref="SearchAsync"/>
    /// asks (one outcome per source, in name order; never throws but for <paramref name="cancellationToken"/>;
    /// nothing stored), but only the query family of that kind: the movie tiers (<c>ReleaseSearchPlanning.For</c>,
    /// the year as the source takes it) for a film, the TV tiers (<c>ReleaseSearchPlanning.ForTv</c>, no season)
    /// for a show. The other family would only cost a request whose releases can't be the work.
    /// </summary>
    /// <param name="text">The title, trimmed and not blank.</param>
    /// <param name="year">A film's year; <c>null</c> for none. A show's search never sends one (ADR 0020).</param>
    /// <param name="kind">Whether the work is a film (<see cref="ReleaseKind.Movie"/>) or a show
    /// (<see cref="ReleaseKind.Tv"/>).</param>
    /// <param name="cancellationToken">The pass's cancellation.</param>
    Task<IReadOnlyList<ReleaseSourceOutcome>> SearchKindAsync(
        string text, int? year, ReleaseKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-33 — asks each enabled release source for its newest releases: one query each
    /// (<c>ReleaseSearchPlanning.ForLatest</c>), in its movie categories when <paramref name="movies"/>, its TV
    /// categories when <paramref name="tv"/>. Outcomes, order, failures and storage as <see cref="SearchAsync"/>'s.
    /// </summary>
    /// <param name="movies">Whether films are wanted.</param>
    /// <param name="tv">Whether shows are wanted.</param>
    /// <param name="cancellationToken">The pass's cancellation.</param>
    Task<IReadOnlyList<ReleaseSourceOutcome>> LatestAsync(bool movies, bool tv, CancellationToken cancellationToken = default);
}
