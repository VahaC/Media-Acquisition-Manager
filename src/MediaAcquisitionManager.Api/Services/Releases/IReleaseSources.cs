using MediaAcquisitionManager.Core.Abstractions.Media;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>
/// Asks every enabled release source for one title (MAM-10). The one place the sources are asked, shared
/// by the plain release search and the stored search (MAM-12), so both ask the same way: concurrently,
/// each answering for itself, every secret masked out of what it says.
/// </summary>
public interface IReleaseSources
{
    /// <summary>
    /// Asks each enabled release source, in name order, concurrently, by the queries <c>ReleaseSearchPlanning</c>
    /// makes from what that source can search by; one outcome per source, in that order. Never throws but
    /// for <paramref name="cancellationToken"/>: a source that fails, or that the app can't ask, answers
    /// with its own status and masked sentence while the others still list theirs. Nothing is stored.
    /// </summary>
    /// <param name="text">The title, already validated and trimmed (<see cref="ReleaseSearchTerms"/>).</param>
    /// <param name="year">The year; <c>null</c> for none.</param>
    /// <param name="identifiers">The ids asked for, IMDb before TMDb.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    Task<IReadOnlyList<ReleaseSourceOutcome>> SearchAsync(
        string text,
        int? year,
        IReadOnlyList<MediaIdentifier> identifiers,
        CancellationToken cancellationToken = default);
}
