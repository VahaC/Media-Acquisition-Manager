using System.Security.Cryptography;
using System.Text;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-25 — the id Jellyfin gives the item of a type at a path: <c>new Guid(MD5(UTF-16 of the type's full name + the
/// path))</c> in its 32-hex-digit form (<c>LibraryManager.GetNewItemId</c>, with case-sensitive ids, the default), which the
/// adapter reads a new file's item by (ADR 0035). Every movie and episode file the vendor samples recorded has the id its
/// path computes, as did 880 of 880 movies and 440 of 440 episode files in the live check of 2026-10-10.
/// </summary>
/// <remarks>
/// The formula is written out here again on purpose, not borrowed from the adapter: the fake Jellyfin files its planted items
/// under these ids, so an adapter whose own copy drifted would find nothing and fail its tests, instead of agreeing with
/// itself. <c>FakeJellyfinServerFilesTests</c> pins this copy against the recorded samples.
/// </remarks>
public static class JellyfinItemIds
{
    /// <summary>The full name of Jellyfin's film type.</summary>
    public const string MovieType = "MediaBrowser.Controller.Entities.Movies.Movie";

    /// <summary>The full name of Jellyfin's episode type.</summary>
    public const string EpisodeType = "MediaBrowser.Controller.Entities.TV.Episode";

    /// <summary>The full name of Jellyfin's plain video type: a home video, an extra other than a trailer.</summary>
    public const string VideoType = "MediaBrowser.Controller.Entities.Video";

    /// <summary>The full name of Jellyfin's trailer type: an extra in a <c>trailers</c> folder or named <c>-trailer</c>,
    /// which Jellyfin resolves as a type of its own.</summary>
    public const string TrailerType = "MediaBrowser.Controller.Entities.Trailer";

    /// <summary>The id Jellyfin gives the item of <paramref name="typeFullName"/> at <paramref name="path"/>, as its
    /// answers carry ids: 32 lower-case hexadecimal digits.</summary>
    public static string Of(string typeFullName, string path) =>
        new Guid(MD5.HashData(Encoding.Unicode.GetBytes(typeFullName + path))).ToString("N");
}
