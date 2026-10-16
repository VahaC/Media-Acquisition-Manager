namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — the MCP tools' own sentences, for the failures the HTTP API answers without one: a bare 404, a Send the
/// Download dialog would disable, arguments that don't fit a tool. Each says what to call instead, as the reader is an
/// AI assistant that can act on it. A service's own sentence (<c>result.Error</c>) goes out as it is, and a token's
/// refusal uses <c>PersonalAccessTokenErrors</c>. English constants until MAM-47 localises the server's sentences.
/// </summary>
/// <remarks>
/// A missing row and another user's get the same sentence, as the HTTP API gives both the same bare 404: neither
/// confirms that the row exists.
/// </remarks>
public static class McpToolErrors
{
    /// <summary>A job id that names none of the caller's jobs: <c>get_job</c>, <c>cancel_job</c>.</summary>
    public const string JobNotFound =
        "There is no download job with this id among yours; list_jobs lists them with their ids.";

    /// <summary>A search id that names none of the caller's searches of the last day: <c>list_search_releases</c>.</summary>
    public const string SearchNotFound =
        "There is no search with this id among yours from the last day; run search_releases again.";

    /// <summary>
    /// A send to a destination whose download client can't see its folder, which the Download dialog doesn't let
    /// through (its Send is disabled): the dialog's own sentence (<c>overwrite.NotCheckedFolderNotSeen</c>), so the
    /// assistant and the web app tell the user the same thing.
    /// </summary>
    public const string SendHeldFolderNotSeen =
        "This destination's download client can't see its folder, so nothing will be sent there until the owner fixes the destination under Destinations in the user menu.";

    /// <summary>
    /// Arguments the SDK couldn't bind to <paramref name="toolName"/>'s parameters — a value of the wrong type, a name
    /// that isn't one of an enum's, a required argument missing — with the binder's own <paramref name="reason"/>, which
    /// names the type it expected or the argument that is missing.
    /// </summary>
    public static string ArgumentsDontFit(string toolName, string reason) =>
        $"The arguments don't fit {toolName}: {reason} Check them against the tool's input schema and call it again.";
}
