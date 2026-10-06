using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-34 — a media library that can identify one item as a catalog film or show, the way its own web interface does:
/// the optional capability the owner's TMDb links are applied through. A further interface of the adapter class, found
/// with <c>registry.Find&lt;IMediaLibraryItemIdentifier&gt;(adapterType)</c> (ADR 0005); a library type without it keeps
/// its links in the app only.
/// </summary>
/// <remarks>
/// <para>
/// Identify (Vasyl, 2026-10-06, ADR 0026): the library is asked for the catalog's own result for the id, and the
/// result replaces the item's title, overview, images and ids, in the language of the item's library — exactly what
/// pressing "Identify" in the library does. It replaces the earlier rule of 2026-10-05 that set the ids alone and asked
/// for a lookup of the missing metadata. The contract has one member that changes anything, and no way to ask for more or
/// less than the library's own button does: no flag for the images, no edit of a single field, no refresh of its own, no
/// delete. Files and folders are never touched; a library that saves beside the media is refused before anything is sent.
/// </para>
/// <para>
/// All methods return result types and never throw on expected failures (unreachable, refused, the item gone, no
/// match); only a cancellation by the caller propagates, as <see cref="OperationCanceledException"/>. A caller's mistake
/// — a kind <see cref="IdentifiableKinds"/> leaves out, a scheme the kind can't take, a malformed value, a blank item
/// id — throws an <see cref="ArgumentException"/> before anything is sent. Every message is one readable English sentence
/// with the call's secrets already masked. The caller runs <see cref="IIntegrationAdapter.TestConnectionAsync"/> first:
/// the version gate (MAM-8).
/// </para>
/// <para>
/// A library says which kinds of item it identifies (<see cref="IdentifiableKinds"/>), so the caller — not a check of
/// which product it is — keeps every other link in the app: Jellyfin identifies films and series, never an episode item.
/// </para>
/// </remarks>
public interface IMediaLibraryItemIdentifier
{
    /// <summary>
    /// The kinds of item this library identifies: a film (<see cref="MediaWorkKind.Movie"/>) and a show
    /// (<see cref="MediaWorkKind.Series"/>). A link on an item of any other kind stays in the app, and an identification
    /// of one is a caller's mistake. Never an episode item: the library has no search for an episode, and its Apply takes
    /// an item id without a type check, so an episode would be given its series' ids.
    /// </summary>
    IReadOnlyList<MediaWorkKind> IdentifiableKinds { get; }

    /// <summary>
    /// Why a link on an item of <paramref name="kind"/> — a kind <see cref="IdentifiableKinds"/> leaves out — stays in
    /// the app: one readable sentence, shown to the owner beside the link.
    /// </summary>
    string UnidentifiableKindReason(MediaWorkKind kind);

    /// <summary>
    /// One identification: reads the item, checks what may never be changed, searches the library's own provider for the
    /// id, chooses the one result that carries exactly that id and — unless the item already shows it — sends Apply
    /// <b>once</b>, then reads the item again to confirm. Never sends Apply twice, whatever happens to the answer (house
    /// rule 8).
    /// </summary>
    /// <remarks>
    /// A failure status says nothing was sent to Apply and carries no value. A value says the identification was decided:
    /// <see cref="LibraryIdentifyOutcome.Identified"/> and <see cref="LibraryIdentifyOutcome.AlreadyIdentified"/> are the
    /// successes; <see cref="LibraryIdentifyOutcome.NoMatch"/> and <see cref="LibraryIdentifyOutcome.Refused"/> are the
    /// library's or the app's refusal with nothing sent; <see cref="LibraryIdentifyOutcome.NotConfirmed"/> says Apply left
    /// the app and the item read again doesn't show the match. <see cref="IntegrationCallStatus.NotFound"/> when the
    /// library no longer has the item. Apply may run for minutes (a series takes about a second per episode), so a caller
    /// never waits for it inside a request.
    /// </remarks>
    Task<IntegrationCallResult<LibraryIdentification>> IdentifyAsync(
        IntegrationConnectionSettings settings, LibraryIdentifyRequest request, CancellationToken cancellationToken = default);
}
