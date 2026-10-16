namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — a write open to a personal access token with Read and download access, and only to that: sending a download
/// (Keep both by construction), cancelling following, and marking a recommendation Not interested. A Read only token
/// gets 403 with <see cref="PersonalAccessTokenErrors.ReadOnly"/> (ADR 0040).
/// </summary>
/// <remarks>
/// <para>
/// Endpoint metadata, read by <see cref="PersonalAccessTokenAccessHandler"/>. Deny by default: a write without this
/// mark, one added tomorrow included, refuses every token; putting it on a new endpoint is a decision for the ADR.
/// </para>
/// <para>
/// MAM-45 — the same mark on an MCP tool method opens that tool to Read and download only (the MCP tool gate reads it).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PersonalAccessTokenWriteAttribute : Attribute;
