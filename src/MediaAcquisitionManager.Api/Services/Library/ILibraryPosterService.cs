namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// MAM-35 — the posters of the Library page's works, served by the app itself (ADR 0027): each is the library's own
/// image of one of the work's items, fetched from the library the owner saved, so the browser never talks to the library,
/// the library's key never leaves the server, and the page's CSP keeps <c>img-src 'self' data:</c>. Expected failures come
/// back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// <para>
/// Only a file name some poster row holds — an item id and the tag a sync read for it (<c>LibraryPosterNames</c>) — is
/// served, so the endpoint fetches nothing a library didn't report and reads no file it didn't write. A poster is fetched
/// once, through its library's adapter (<c>IMediaLibraryProvider.GetPosterAsync</c>), and kept in
/// <c>&lt;data&gt;/library-posters/</c>: a new image gets a new tag, so a kept file is never asked for again once the next
/// sync has read it.
/// </para>
/// <para>
/// Kept files are never pruned: a 342-wide JPEG is about 35 KB, so even a library's every work over years stays far below
/// the library's own size.
/// </para>
/// </remarks>
public interface ILibraryPosterService
{
    /// <summary>
    /// The poster <paramref name="fileName"/> names (<c>{itemId}-{tag}.jpg</c>): from the cache, else fetched from the
    /// library that reported it and cached. <see cref="LibraryPosterStatus.NotFound"/> for a name that is no poster row's,
    /// a poster the library doesn't have, or one not cached while its library is switched off or gone;
    /// <see cref="LibraryPosterStatus.Failed"/> when the fetch failed — with the library's sentence, masked, for the
    /// installation owner <paramref name="callerUserId"/>, and a plain one for anyone else.
    /// </summary>
    Task<LibraryPosterResult> GetAsync(string fileName, Guid callerUserId, CancellationToken cancellationToken = default);
}
