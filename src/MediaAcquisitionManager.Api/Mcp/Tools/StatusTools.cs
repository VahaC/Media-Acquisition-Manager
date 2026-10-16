using System.ComponentModel;
using System.Security.Claims;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp.Tools;

/// <summary>MAM-45 — <c>get_status</c>: who the caller is and how the sources a user can see stand.</summary>
/// <remarks>
/// What a member reads too, shaped by the services as the web app shapes it — no Settings health, which only the owner
/// reads there and no token reads at all (Vasyl's decision f, ADR 0040). Built per call by the SDK, with the request's
/// services.
/// </remarks>
[McpServerToolType]
public sealed class StatusTools(
    ApplicationDbContext db,
    ILibrarySyncService librarySync,
    IRecommendationRunService recommendationRuns,
    IRecommendationListService recommendationLists,
    McpToolResults results)
{
    [McpServerTool(
        Name = "get_status",
        Title = "Status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(StatusToolResult))]
    [Description(
        "Tells who you are acting as in MAM — the user, whether they are the installation owner, and this access token's "
        + "name, access and expiry — with MAM's version, how each media library source last synced, the latest "
        + "recommendations run, and the release sources whose last recommendations check failed. Call it when you need to "
        + "know what this token may do, or when another tool's answer suggests a source is down. Download clients and "
        + "settings are not reported: only the web app shows them, to the owner.")]
    public async Task<CallToolResult> GetStatusAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        // Always a token: /mcp refuses a session before any tool runs.
        var tokenId = user.GetPersonalAccessTokenId()
            ?? throw new InvalidOperationException("get_status runs on a personal access token only.");
        // The row authentication has just found, read again for its name and expiry, which the claims don't carry.
        var caller = await db.PersonalAccessTokens.AsNoTracking()
            .Where(token => token.Id == tokenId && token.UserId == userId)
            .Join(db.Users, token => token.UserId, account => account.Id, (token, account) => new { account.UserName, token.Name, token.Access, token.ExpiresUtc })
            .SingleAsync(cancellationToken);
        var isOwner = await db.IsInstallationOwnerAsync(userId, cancellationToken);

        var status = new StatusToolResult(
            new McpCallerDto(caller.UserName, isOwner, caller.Name, caller.Access, caller.ExpiresUtc),
            McpServerIdentity.Version,
            await librarySync.ListSourcesAsync(userId, cancellationToken),
            await recommendationRuns.GetLatestAsync(userId, cancellationToken),
            await recommendationLists.ListFailedSourcesAsync(userId, cancellationToken));

        var who = isOwner ? "the installation owner" : "a member";
        var access = caller.Access == PersonalAccessTokenAccess.ReadAndDownload ? "Read and download" : "Read only";
        return results.Success(
            $"You act as {caller.UserName} ({who}) with the {access} token “{caller.Name}”, on MAM {McpServerIdentity.Version}; "
            + $"{Count(status.Library.Items.Count, "library source")}, {Count(status.FailedRecommendationSources.Count, "release source")} failing.",
            status);
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
