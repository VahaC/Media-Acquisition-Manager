using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-34 — what one <see cref="IMediaLibraryItemIdentifier.IdentifyAsync"/> is asked to identify.</summary>
/// <param name="SourceItemId">The library's id of the item (a Guid in Jellyfin; anything else is a caller's
/// mistake).</param>
/// <param name="Kind">What the item must be: <see cref="MediaWorkKind.Movie"/> or <see cref="MediaWorkKind.Series"/>.
/// An item of another type is refused unidentified.</param>
/// <param name="Identifier">The one id the identification is by. Only these pairs are allowed, and any other is a caller's
/// mistake thrown before anything is sent — the namespace guard of ADR 0026, enforced by the contract: a TV id never
/// reaches the film search and a film id never the series search.
/// <list type="bullet">
/// <item><description>(<see cref="MediaWorkKind.Movie"/>, <see cref="MediaIdentifierScheme.Tmdb"/>): a film's TMDb id,
/// through the library's film search and its TMDb provider.</description></item>
/// <item><description>(<see cref="MediaWorkKind.Series"/>, <see cref="MediaIdentifierScheme.TmdbTv"/>): a show's TMDb id,
/// through the library's series search and its TMDb provider.</description></item>
/// <item><description>(<see cref="MediaWorkKind.Movie"/>, <see cref="MediaIdentifierScheme.Imdb"/>): an episode's own IMDb
/// id on a film item (an "episode-movie": a library that lists an episode file as a film), through the film search and the
/// Open Movie Database — the only route recorded live for these.</description></item>
/// </list></param>
/// <param name="ProtectHeldIds"><c>true</c>: refuse (<see cref="LibraryIdentifyRefusal.ConflictingId"/>) when the item
/// holds an id of its own that the identification would discard. <c>false</c>: the owner's own word — Identify replaces
/// the ids as the library's button does. The caller sets it; the adapter decides nothing about who asked.</param>
public sealed record LibraryIdentifyRequest(string SourceItemId, MediaWorkKind Kind, MediaIdentifier Identifier, bool ProtectHeldIds);
