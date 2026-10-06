using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the two small rules the sync's judging and the audit rows of an identification share, kept in one place so
/// they can't disagree: which id a link is judged by, and how the ids an identification found and left are recorded.
/// </summary>
public static class LibraryLinkKeys
{
    /// <summary>
    /// The id a sync judges <paramref name="link"/> by and an identification leaves in its item — its "identified key": a
    /// link to the work, its own TMDb id; an episode's link, the episode's IMDb id once TMDb gave one. <c>null</c> for a
    /// link that identifies nothing of its own (an episode's without its IMDb id).
    /// </summary>
    public static MediaIdentifier? IdentifiedKey(LibraryItemLinkEntity link) =>
        link.Target == LibraryLinkTarget.Work
            ? new MediaIdentifier(link.Scheme, link.Value)
            : link.EpisodeImdbId is { } episodeImdbId ? new MediaIdentifier(MediaIdentifierScheme.Imdb, episodeImdbId) : null;

    /// <summary>An audit row's ids: <paramref name="before"/> as what the change found, <paramref name="after"/> as what it
    /// left, in their order.</summary>
    public static IReadOnlyList<AuditIdentifier> AuditIdentifiers(IEnumerable<MediaIdentifier> before, IEnumerable<MediaIdentifier> after) =>
    [
        .. before.Select(identifier => new AuditIdentifier(AuditIdentifierRole.Before, identifier.Scheme, identifier.Value)),
        .. after.Select(identifier => new AuditIdentifier(AuditIdentifierRole.After, identifier.Scheme, identifier.Value)),
    ];
}
