using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — the MCP tools' own sentences, for the failures the HTTP API answers without one: a bare 404, a Send the
/// Download dialog would disable, arguments that don't fit a tool. Each says what to call instead, as the reader is an
/// AI assistant that can act on it. A service's own sentence (<c>result.Error</c>) goes out as it is, and a token's
/// refusal uses <c>PersonalAccessTokenErrors</c>. MAM-47 (ADR 0042) — messages of <c>mcp.*</c>, which a tool writes in
/// the language of the MCP request: English unless the client sends <c>Accept-Language</c>.
/// </summary>
/// <remarks>
/// A missing row and another user's get the same sentence, as the HTTP API gives both the same bare 404: neither
/// confirms that the row exists.
/// </remarks>
public static class McpToolErrors
{
    /// <summary>A job id that names none of the caller's jobs: <c>get_job</c>, <c>cancel_job</c>.</summary>
    public static readonly Message JobNotFound = Message.Of("mcp.job-not-found");

    /// <summary>A search id that names none of the caller's searches of the last day: <c>list_search_releases</c>.</summary>
    public static readonly Message SearchNotFound = Message.Of("mcp.search-not-found");

    /// <summary>
    /// A send to a destination whose download client can't see its folder, which the Download dialog doesn't let
    /// through (its Send is disabled): the dialog's own sentence (<c>overwrite.NotCheckedFolderNotSeen</c>), so the
    /// assistant and the web app tell the user the same thing.
    /// </summary>
    public static readonly Message SendHeldFolderNotSeen = Message.Of("mcp.send-held-folder-not-seen");

    /// <summary>MAM-47 — how much of the binder's message the sentence quotes. It names the type it expected by its full
    /// name, a nullable enum's runs to about 170 characters; the cut only keeps a pathological message from filling the
    /// assistant's context.</summary>
    private const int ReasonLength = 500;

    /// <summary>
    /// Arguments the SDK couldn't bind to <paramref name="toolName"/>'s parameters — a value of the wrong type, a name
    /// that isn't one of an enum's, a required argument missing — with the binder's own <paramref name="reason"/>, which
    /// names the type it expected or the argument that is missing. The binder's words are .NET's, shown as they are
    /// (<see cref="VendorText"/>).
    /// </summary>
    public static Message ArgumentsDontFit(string toolName, string reason) =>
        Message.Of("mcp.arguments-dont-fit", ("tool", toolName), ("reason", VendorText.Clean(reason, ReasonLength)));
}
