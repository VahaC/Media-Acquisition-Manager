using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — what TMDb says of a numbered episode's own IMDb id, the id an episode's link writes into a film item
/// (Vasyl's decision of 2026-10-06): the id, none — TMDb gives the episode none, or knows no such episode —, or the
/// sentence why TMDb couldn't be asked.
/// </summary>
/// <param name="ImdbId">The episode's IMDb id; <c>null</c> when TMDb gives none or couldn't be asked.</param>
/// <param name="Error">Why TMDb couldn't be asked, its secrets masked; <c>null</c> when it answered.</param>
public sealed record LibraryLinkEpisodeImdb(string? ImdbId, string? Error)
{
    /// <summary>
    /// Asks <paramref name="catalog"/> for the episode <paramref name="episodeNumber"/> of season
    /// <paramref name="seasonNumber"/> of the show <paramref name="showId"/>. Read live, never from the catalog's cache:
    /// it is asked once per link, when the link is saved or first written, and kept on the link.
    /// </summary>
    public static async Task<LibraryLinkEpisodeImdb> AskAsync(
        CatalogConnection catalog, string showId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken)
    {
        var read = await catalog.Catalog.GetEpisodeAsync(catalog.Settings, showId.Trim(), seasonNumber, episodeNumber, cancellationToken);
        if (read.IsSuccess)
            return new LibraryLinkEpisodeImdb(read.Value!.ImdbId?.Trim() is { } imdbId && MediaIdentifierRules.IsImdbId(imdbId) ? imdbId : null, null);

        // An episode TMDb doesn't know has no IMDb id it could give: the link stays in the app, as for one it gives none.
        return read.Status == IntegrationCallStatus.NotFound
            ? new LibraryLinkEpisodeImdb(null, null)
            : new LibraryLinkEpisodeImdb(null, SecretRedaction.Redact(read.Error!, catalog.Settings.Secrets));
    }
}
