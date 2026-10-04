using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>
/// Asks release sources live for their categories (MAM-32), the sections the owner maps to destinations
/// (ADR 0020): read from the source's capabilities each time, never stored, so a section the tracker adds
/// or renames shows the next time anyone looks.
/// </summary>
/// <remarks>
/// Reads only. A source that couldn't be asked is an answer, never an exception — switched off, a type
/// this version can't search through, a stored secret it can't read, or the source's own failure, its
/// sentence masked of every secret of the call. Only the caller's cancellation propagates. It checks
/// nothing about who calls.
/// </remarks>
public interface IReleaseSourceCategoryReader
{
    /// <summary>
    /// What each of <paramref name="sources"/>, release search integrations, lists, in their order: every
    /// enabled one asked at the same time, a switched-off one not at all.
    /// </summary>
    Task<IReadOnlyList<ReleaseSourceCategories>> ReadAsync(
        IReadOnlyList<IntegrationConnectionEntity> sources, CancellationToken cancellationToken = default);
}
