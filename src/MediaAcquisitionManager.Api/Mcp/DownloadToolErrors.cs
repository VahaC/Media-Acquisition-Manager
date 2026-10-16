namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — the sentences of <c>get_download_options</c> and <c>send_download</c> that neither the download service nor
/// <see cref="McpToolErrors"/> has. English constants until MAM-47 localises the server's sentences.
/// </summary>
public static class DownloadToolErrors
{
    /// <summary>
    /// A <c>destinationId</c> for the overwrite check that isn't one of the release's options. The Download dialog can only
    /// check a destination it offers; answering the check of another would read as if the release could go there.
    /// </summary>
    public const string DestinationNotOffered =
        "This destination isn't one of this release's download options; give one of the destinations get_download_options lists for it.";
}
