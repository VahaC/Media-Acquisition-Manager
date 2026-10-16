using System.Reflection;
using ModelContextProtocol.Protocol;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — how the MCP endpoint introduces itself in the <c>initialize</c> (and <c>server/discover</c>) answer: the
/// name an assistant's configuration and permission rules use (<c>mcp__mam__…</c> in Claude Code), a title for people,
/// and the version of this build.
/// </summary>
/// <remarks>
/// Set explicitly: left unset, the SDK names the server after the entry assembly, "MediaAcquisitionManager.Api", which
/// is neither the name the README tells people to configure nor a version anyone recognises.
/// </remarks>
public static class McpServerIdentity
{
    /// <summary>The server's name: short, as a client prefixes every tool name with it.</summary>
    public const string Name = "mam";

    /// <summary>The name people read in a client's server list.</summary>
    public const string Title = "Media Acquisition Manager";

    /// <summary>
    /// This build's informational version — the release a Docker build is stamped with (<c>0.5.0+&lt;revision&gt;</c>), or
    /// <c>0.0.0-dev</c> for a local build — the same value <c>get_status</c> reports.
    /// </summary>
    public static string Version { get; } =
        typeof(McpServerIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0-dev";

    /// <summary>The identity, as the SDK takes it. A new instance per call: the options that hold it are built per
    /// request in stateless mode.</summary>
    public static Implementation Create() => new() { Name = Name, Title = Title, Version = Version };
}
