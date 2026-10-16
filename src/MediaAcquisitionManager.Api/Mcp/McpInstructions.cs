using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — the server instructions an MCP client hands its model when it connects: what MAM is, the order the tools
/// are meant to be used in, and what this token may and may not do — so the model neither offers a send a Read only
/// token can't make nor promises a replacement no tool makes.
/// </summary>
/// <remarks>
/// Per token: the SDK asks for them on every POST in stateless mode (<c>ConfigureSessionOptions</c>), so they follow the
/// access of the token that connected. English, as the tools' descriptions, with a line telling the model to answer in
/// the user's own language. Under 2,000 characters each, well inside Claude Code's 4,096, and pinned by
/// <c>McpEndpointHttpTests</c>.
/// </remarks>
public static class McpInstructions
{
    private const string About =
        "MAM (Media Acquisition Manager) is this household's media library helper: it knows what the media servers hold, "
        + "asks the release sources (torrent trackers) for releases, sends a chosen one to the download client and follows "
        + "it as a download job. Every tool acts as the user this access token belongs to and sees what that user sees.\n\n";

    private const string Flow =
        "To find something: search_library first, to see which copies the library already holds; then search_releases, "
        + "which asks every release source and can take a minute, and returns a searchId; then list_search_releases to "
        + "filter, sort or page that search without searching again; then get_download_options for the release the user "
        + "likes: the download clients and destinations the web app's Download dialog offers, its preselection, and "
        + "whether that destination can be checked for files the release would write over.\n\n";

    private const string ReadAndDownloadPart =
        "Only after the user has confirmed the exact release, client and destination, call send_download with those ids; "
        + "on a retry of the same send, pass the same requestId. list_jobs and get_job say where downloads stand and why "
        + "one stopped. cancel_job only stops following a job: the torrent and its files stay in the download client. "
        + "dismiss_recommendation marks a recommendation Not interested.\n\n";

    private const string ReadOnlyPart =
        "This token is read only: it reads everything its user reads, but can't send downloads, cancel following or mark "
        + "Not interested. For those the user creates a token with “Read and download” access on their Profile page. "
        + "list_jobs and get_job say where downloads stand and why one stopped.\n\n";

    private const string Rest =
        "list_duplicates and list_recommendations read the Duplicates and Recommendations pages; get_status says who you "
        + "are and how the library sources stand. Replacing, quarantining, restoring or deleting copies, and every "
        + "setting, are done in the web app; no tool does them. Sizes are in bytes, times in UTC. Reply in the user's "
        + "language.";

    /// <summary>For a token with Read and download access.</summary>
    public const string ReadAndDownload = About + Flow + ReadAndDownloadPart + Rest;

    /// <summary>For a Read only token.</summary>
    public const string ReadOnly = About + Flow + ReadOnlyPart + Rest;

    /// <summary>The instructions for a token of <paramref name="access"/>; an access that names nothing gets the Read
    /// only ones, as the access check counts it as the least.</summary>
    public static string For(PersonalAccessTokenAccess? access) =>
        access == PersonalAccessTokenAccess.ReadAndDownload ? ReadAndDownload : ReadOnly;
}
