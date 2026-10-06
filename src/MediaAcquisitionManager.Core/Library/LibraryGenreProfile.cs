using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-34 — the genre a library mostly holds (animation, documentaries), from the TMDb genres of the works in it that
/// already have a TMDb id (decision 3): the "Animation or Documentary against the library" evidence of a suggestion.
/// </summary>
/// <remarks>
/// Never from the library's name ("Мульфільми" says nothing a sync could read: ADR 0017, a name decides nothing), and never
/// from the server's library type, which is "mixed" for both the cartoons and the documentaries (VendorSamples). Animation
/// is asked first, then Documentary: an animated documentary in a cartoon library counts for animation.
/// </remarks>
public static class LibraryGenreProfile
{
    /// <summary>
    /// How many identified works a library needs before it has a genre: two cartoons that happen to sit among films say
    /// nothing about the films.
    /// </summary>
    public const int MinimumIdentifiedWorks = 3;

    /// <summary>
    /// The share of the identified works that must hold the genre: a clear majority, so a film library with a few
    /// cartoons in it isn't a cartoon library, while a cartoon library with a live-action special or two still is.
    /// </summary>
    public const double DominantShare = 0.6;

    // Animation asked first (see the remarks).
    private static readonly CatalogGenreRole[] RolesInOrder = [CatalogGenreRole.Animation, CatalogGenreRole.Documentary];

    /// <summary>
    /// <see cref="CatalogGenreRole.Animation"/> or <see cref="CatalogGenreRole.Documentary"/> when at least
    /// <see cref="MinimumIdentifiedWorks"/> works are given and at least <see cref="DominantShare"/> of them hold it;
    /// <c>null</c> otherwise.
    /// </summary>
    /// <param name="identifiedWorksRoles">What each identified work's genres make it, one entry per work.</param>
    public static CatalogGenreRole? Of(IReadOnlyList<IReadOnlyList<CatalogGenreRole>> identifiedWorksRoles)
    {
        ArgumentNullException.ThrowIfNull(identifiedWorksRoles);
        if (identifiedWorksRoles.Count < MinimumIdentifiedWorks)
            return null;

        foreach (var role in RolesInOrder)
        {
            // A share against the share, not a count against a product: 3 of 5 is exactly the 0.6 it should be.
            var share = (double)identifiedWorksRoles.Count(roles => roles.Contains(role)) / identifiedWorksRoles.Count;
            if (share >= DominantShare)
                return role;
        }

        return null;
    }
}
