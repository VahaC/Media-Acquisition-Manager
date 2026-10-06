using MediaAcquisitionManager.Api.Services.Audit;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — one library item's TMDb link written into its library, or what the app wrote for it put back: the one
/// path every write takes — the owner's Save, Retry and Write again, a removal, and the background writer of "Accept
/// all confident" and of the catch-up — so each is gated, sent and recorded the same way. A write that leaves the
/// link's ids in the item asks the library to look the item up (Vasyl's decision of 2026-10-06).
/// </summary>
/// <remarks>
/// <para>
/// The caller claims the link first (R9): it moves it into <c>Writing</c> under a new <c>WriteAttemptId</c>, by a
/// conditional update or by saving it so, and only the claimer calls the library. Every state this writer settles is a
/// conditional update on that claim, so a write that ran late can never overwrite what a later one set.
/// </para>
/// <para>
/// The library is reached the way a sync reaches it: the capability found on the adapter type
/// (<c>IMediaLibraryIdentifierWriter</c>, ADR 0005) — a type without it keeps its links in the app, and so does one for
/// the kinds of item it doesn't write (<c>WritableKinds</c>; Jellyfin: films and series, <see cref="LibraryLinkWritability"/>)
/// —, the integration switched on (R12), its secrets opened, and its connection test passed first (the version gate, MAM-8). Every write
/// that left the app is recorded (<c>LibraryIdsWritten</c>, R10), confirmed or not, so a later removal knows what to put
/// back. The caller passes <see cref="CancellationToken.None"/> once the link is saved: a write runs to its end whether
/// or not the browser is still there (ADR 0013).
/// </para>
/// </remarks>
public interface ILibraryLinkWriter
{
    /// <summary>
    /// Writes the claimed link <paramref name="linkId"/> (claimed under <paramref name="attemptId"/>) into its library
    /// item and settles its state: <c>Written</c> when the read after the write shows it — and then asks the library to
    /// look the item's missing metadata up, recording when and why it didn't take it (a failed lookup leaves the link
    /// <c>Written</c>) —, <c>NotWritten</c> with the sentence otherwise; but a write that sent nothing to an item whose
    /// ids the last sync read there leaves the link <c>InLibrary</c>, the sentence its lookup's error. An episode's link
    /// writes the episode's own IMDb id, asked of TMDb first when the link doesn't hold it. A link that isn't written — an
    /// episode's link without its numbers or whose episode TMDb gives no IMDb id, a link on a kind of item its library
    /// doesn't write (Jellyfin: an episode item), or one of a library type that can't set ids — is settled
    /// <c>KeptInApp</c>, after whatever the app wrote for it before is put back (a film link replaced by an episode's). <paramref name="actor"/> is recorded on the audit
    /// rows of what was sent.
    /// </summary>
    Task<LibraryLinkWriteResult> WriteAsync(Guid linkId, Guid attemptId, AuditActor? actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts back, before the claimed link <paramref name="linkId"/> is removed, what the app wrote for it (R11): each id
    /// only while the item still holds what the app wrote (decision 13). Records nothing and settles nothing: the caller
    /// removes the link with its audit rows when this succeeds, and gives the link back its state when it doesn't. A link
    /// whose item the library no longer reports, or that was never sent — one kept in the app —, needs nothing.
    /// </summary>
    Task<LibraryLinkRestoreResult> RestoreAsync(Guid linkId, Guid attemptId, CancellationToken cancellationToken = default);
}
