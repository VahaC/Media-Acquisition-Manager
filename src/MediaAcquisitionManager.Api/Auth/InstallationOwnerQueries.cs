using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// Whether a caller is the installation owner, for a read that every signed-in user may make but whose
/// answer leaves out what only the owner may see (MAM-8: the Library's sync runs, sources and works).
/// </summary>
public static class InstallationOwnerQueries
{
    /// <summary>
    /// <c>true</c> when <paramref name="userId"/> owns the installation. Read from the installation row
    /// on every call, as <see cref="InstallationOwnerFilter"/> does: nothing a token says makes its
    /// holder the owner.
    /// </summary>
    public static Task<bool> IsInstallationOwnerAsync(this ApplicationDbContext db, Guid userId, CancellationToken cancellationToken = default) =>
        db.Installations.AnyAsync(installation => installation.OwnerUserId == userId, cancellationToken);
}
