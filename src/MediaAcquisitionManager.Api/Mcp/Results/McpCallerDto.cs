using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Mcp.Results;

/// <summary>MAM-45 — who an MCP tool acts as: the token's user, and the token itself, so an assistant can tell the user
/// what it may do and until when.</summary>
/// <param name="UserName">The user the token acts as, by sign-in name.</param>
/// <param name="IsInstallationOwner">Whether that user is the installation owner, who reads more than a member: a
/// source's error, a path problem.</param>
/// <param name="TokenName">What the user called the token on their Profile page.</param>
/// <param name="TokenAccess">What the token may do: <c>ReadOnly</c>, or <c>ReadAndDownload</c>, which may also send a
/// download, cancel following and mark Not interested.</param>
/// <param name="TokenExpiresUtc">When the token stops working; <c>null</c> when it never expires.</param>
public sealed record McpCallerDto(
    string UserName,
    bool IsInstallationOwner,
    string TokenName,
    PersonalAccessTokenAccess TokenAccess,
    DateTime? TokenExpiresUtc);
