using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// The destination the Download dialog picks first for a release (MAM-32, ADR 0020): the one its tracker
/// section is mapped to, or the installation's default when none is.
/// </summary>
/// <remarks>
/// <para>
/// A release is listed in a standard category and the source's own section at once ("TV/HD" and
/// «Мультсеріали в HD»); when both are mapped, the source's own wins: it says more — a cartoon series is
/// filed under TV like any show. Two different destinations at the rank that decides is no answer: the
/// dialog leaves the pick to the person rather than guess, and never falls back to a lower rank or the
/// default, which would hide the disagreement.
/// </para>
/// <para>
/// Only a destination the dialog offers can be suggested: one of a switched-off client, or of one that
/// can't take the release, is as good as unmapped.
/// </para>
/// </remarks>
public static class DestinationSuggestion
{
    /// <summary>
    /// The suggested destination among <paramref name="offered"/>, from the sections of the release's source
    /// that hold it (<paramref name="sections"/>, its categories' rows), or <paramref name="defaultId"/>
    /// when none of them is mapped to an offered destination; <c>null</c> for a tie, or when the default is
    /// not offered either.
    /// </summary>
    public static Guid? For(IReadOnlyCollection<DestinationSectionEntity> sections, IReadOnlySet<Guid> offered, Guid? defaultId)
    {
        var usable = sections.Where(section => offered.Contains(section.DestinationMappingId)).ToList();
        foreach (var sourceSpecific in (bool[])[true, false])
        {
            var destinations = usable
                .Where(section => section.IsSourceSpecific == sourceSpecific)
                .Select(section => section.DestinationMappingId)
                .Distinct()
                .Take(2)
                .ToList();
            if (destinations.Count > 0)
                return destinations.Count == 1 ? destinations[0] : null;
        }

        return defaultId is { } id && offered.Contains(id) ? id : null;
    }
}
