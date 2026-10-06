using MediaAcquisitionManager.Api.Services.Audit;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — one library item identified as its TMDb link says: the one path every identification takes. Only the
/// background writer calls it — the owner's Save, Retry and Write again, "Accept all confident" and the repair all
/// queue their links and never wait for the library (a series takes minutes) —, so each is gated, sent and recorded the
/// same way.
/// </summary>
/// <remarks>
/// <para>
/// The caller claims the link first (R9): it moves it into <c>Writing</c> under a new <c>WriteAttemptId</c> by a
/// conditional update, and only the claimer calls the library. Every state this writer settles is a conditional update
/// on that claim, so an identification that ran late can never overwrite what a later one set.
/// </para>
/// <para>
/// The library is reached the way a sync reaches it: the capability found on the adapter type
/// (<c>IMediaLibraryItemIdentifier</c>, ADR 0005) — a type without it keeps its links in the app, and so does one for the
/// kinds of item it doesn't identify (<c>IdentifiableKinds</c>; Jellyfin: films and series, <see cref="LibraryLinkWritability"/>) —,
/// the integration switched on (R12), its secrets opened and its connection test passed first (the version gate, MAM-8).
/// Every identification whose Apply left the app is recorded (<c>LibraryItemIdentified</c>, R10), confirmed or not, and
/// is the app's only record that one was sent. The caller passes <see cref="CancellationToken.None"/> once the link is
/// claimed: an identification runs to its end whether or not anyone is still waiting (ADR 0013), and is never repeated.
/// </para>
/// </remarks>
public interface ILibraryLinkWriter
{
    /// <summary>
    /// Identifies the claimed link <paramref name="linkId"/> (claimed under <paramref name="attemptId"/>) in its library
    /// item and settles its state: <c>Written</c> when the library's own result is on the item — confirmed by reading it
    /// again, or found there already, nothing sent —, <c>NotWritten</c> with the sentence for every refusal, missing match,
    /// failure and unconfirmed identification, <c>KeptInApp</c> for a link no rule lets the library identify (an episode's
    /// without its numbers or whose episode TMDb gives no IMDb id, a link on a kind of item its library doesn't identify, or
    /// one of a library type that can't). An episode's link is identified by the episode's own IMDb id, asked of TMDb first
    /// when the link doesn't hold it. <paramref name="actor"/> is recorded on the audit row of an Apply that was sent.
    /// </summary>
    Task<LibraryLinkWriteResult> WriteAsync(Guid linkId, Guid attemptId, AuditActor? actor, CancellationToken cancellationToken = default);
}
