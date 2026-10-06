using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-34 — a media library that can set an item's registry ids (Jellyfin's <c>ProviderIds</c>): the optional
/// capability the owner's TMDb links are written through. A further interface of the adapter class, found with
/// <c>registry.Find&lt;IMediaLibraryIdentifierWriter&gt;(adapterType)</c> (ADR 0005); a library type without it keeps
/// its links in the app only.
/// </summary>
/// <remarks>
/// <para>
/// Ids only (Vasyl, 2026-10-05): no identify, no metadata refresh, no images, titles or locks — the contract has no
/// member for any of them. Every other field of the item goes back exactly as it was read, but for one the library
/// would rebuild from less than it holds, which is left out so the library keeps its own (Jellyfin's <c>People</c>).
/// </para>
/// <para>
/// All methods return result types and never throw on expected failures (unreachable, refused, the item gone); only a
/// cancellation by the caller propagates, as <see cref="OperationCanceledException"/>. A caller's mistake — a kind
/// <see cref="WritableKinds"/> leaves out, a scheme the kind can't take, a malformed value, no edit, a blank item id —
/// throws an <see cref="ArgumentException"/> before anything is sent. Every message is one readable English sentence
/// with the call's secrets already masked. The caller runs <see cref="IIntegrationAdapter.TestConnectionAsync"/> first:
/// the version gate (MAM-8).
/// </para>
/// <para>
/// A library says which kinds of item it writes (<see cref="WritableKinds"/>), so the caller — not a check of which
/// product it is — keeps every other link in the app: Jellyfin writes films only, as saving a series rewrites each of
/// its seasons and episodes (Vasyl, 2026-10-06).
/// </para>
/// </remarks>
public interface IMediaLibraryIdentifierWriter
{
    /// <summary>
    /// The kinds of item this library's ids are written on, of a film (<see cref="MediaWorkKind.Movie"/>) and a show
    /// (<see cref="MediaWorkKind.Series"/>). A link on an item of any other kind stays in the app, and a change of one
    /// is a caller's mistake.
    /// </summary>
    IReadOnlyList<MediaWorkKind> WritableKinds { get; }

    /// <summary>
    /// Why a link on an item of <paramref name="kind"/> — a kind <see cref="WritableKinds"/> leaves out — stays in the
    /// app: one readable sentence, shown to the owner beside the link.
    /// </summary>
    string UnwritableKindReason(MediaWorkKind kind);

    /// <summary>
    /// One write: reads the item, applies <paramref name="change"/>'s edits to its registry ids only, sends it back
    /// <b>once</b> — never again, whatever happens to the answer (house rule 8) — and reads it again to confirm.
    /// </summary>
    /// <remarks>
    /// <see cref="IntegrationCallStatus.Succeeded"/> whenever the change left the app, confirmed or not
    /// (<see cref="LibraryIdentifierWrite.Sent"/>, <see cref="LibraryIdentifierWrite.Confirmed"/>,
    /// <see cref="LibraryIdentifierWrite.Problem"/>), so the caller records every write that may have landed (R10); a
    /// failure status only when nothing was sent. <see cref="IntegrationCallStatus.NotFound"/> when the library no
    /// longer has the item.
    /// </remarks>
    Task<IntegrationCallResult<LibraryIdentifierWrite>> WriteIdentifiersAsync(
        IntegrationConnectionSettings settings, LibraryIdentifierChange change, CancellationToken cancellationToken = default);
}
