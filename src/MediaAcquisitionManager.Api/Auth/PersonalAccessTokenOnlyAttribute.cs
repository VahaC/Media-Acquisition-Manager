namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-45 — an endpoint for personal access tokens alone, whose own code applies a token's access to each call: the MCP
/// endpoint, where one POST carries any tool call. Every token passes; a signed-in session gets 403 with
/// <see cref="PersonalAccessTokenErrors.SessionNotAccepted"/>.
/// </summary>
/// <remarks>
/// Endpoint metadata, read by <see cref="PersonalAccessTokenAccessHandler"/>, so it works on a minimal endpoint such as
/// <c>/mcp</c>. Neither of the other marks fits there: <see cref="PersonalAccessTokenReadAttribute"/> would let a session
/// in, and <see cref="PersonalAccessTokenWriteAttribute"/> would shut a Read only token out of the reads, as the HTTP
/// level can't see which tool a POST calls. The MCP tool gate judges each call instead (ADR 0040's two levels, per
/// tool).
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PersonalAccessTokenOnlyAttribute : Attribute;
