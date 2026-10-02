namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// Whose sentence a failed download job's error is (MAM-13), which decides who may read it. Stored by name.
/// </summary>
/// <remarks>
/// MAM-10's rule for a service's sentence: it can name the service's address or a redirect target, and the
/// integrations are the owner's, so only the owner reads it; the app's own sentences name nothing of theirs
/// and anyone who sent the release reads them.
/// </remarks>
public enum DownloadJobErrorOrigin
{
    /// <summary>The app's own sentence: a category gone, a torrent the client already has, a link it can't
    /// open, a setting it can't read.</summary>
    App = 1,

    /// <summary>What the release's source answered when its payload was fetched.</summary>
    ReleaseSource = 2,

    /// <summary>What the download client answered.</summary>
    DownloadClient = 3,
}
