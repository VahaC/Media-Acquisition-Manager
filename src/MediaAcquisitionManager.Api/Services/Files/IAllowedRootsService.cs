using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// The allowed roots (MAM-9): the folders inside the app where it may read library files, kept in the
/// database and edited by the installation owner on the Media folders page. None by default, so a fresh
/// installation reads nothing until the owner names a folder. Expected failures come back as result
/// statuses, never as exceptions.
/// </summary>
public interface IAllowedRootsService
{
    /// <summary>Every allowed root in canonical form, in the order the owner entered them.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the allowed roots with <paramref name="paths"/> and records an audit row for
    /// <paramref name="actorUserId"/> in the same save; a list that is the stored one in canonical form
    /// writes nothing. <see cref="AllowedRootsUpdateStatus.Invalid"/>, writing nothing, when the list breaks a
    /// rule of <see cref="Core.Files.AllowedRootRules"/>. Saving starts no inspection: the owner does that
    /// from the Library page.
    /// </summary>
    Task<AllowedRootsUpdateResult> UpdateAsync(IReadOnlyList<string?> paths, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>The response the settings page reads.</summary>
    Task<AllowedRootsResponse> GetAsync(CancellationToken cancellationToken = default);
}
