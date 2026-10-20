using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — the sentences of <c>get_download_options</c> and <c>send_download</c> that neither the download service nor
/// <see cref="McpToolErrors"/> has. MAM-47 (ADR 0042) — messages of <c>downloads.mcp.*</c>, which the tool writes in the
/// language of the MCP request: English unless the client sends <c>Accept-Language</c>.
/// </summary>
public static class DownloadToolErrors
{
    /// <summary>
    /// A <c>destinationId</c> for the overwrite check that isn't one of the release's options. The Download dialog can only
    /// check a destination it offers; answering the check of another would read as if the release could go there.
    /// </summary>
    public static readonly Message DestinationNotOffered = Message.Of("downloads.mcp.destination-not-offered");
}
