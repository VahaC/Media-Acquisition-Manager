using System.ComponentModel;
using System.Security.Claims;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Duplicates;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp.Tools;

/// <summary>MAM-45 — <c>list_duplicates</c>: the web app's Duplicates page, read only — removing a copy stays the
/// owner's, on that page (ADR 0037).</summary>
/// <remarks>
/// The service's list as <c>GET /api/library/duplicates</c> gives it, owner-only sentences shaped for the caller by the
/// service. The filters go to it by name, as the query string carries them, so a value is checked by the same rules and
/// refused with the same sentence. Built per call by the SDK, with the request's services. MAM-47 (ADR 0042) — the
/// summary and the refusal in the request's language (<c>texts</c>); the tool's own descriptions stay English, as an AI
/// reads them.
/// </remarks>
[McpServerToolType]
public sealed class DuplicateTools(ILibraryDuplicateService duplicates, McpToolResults results, IRequestTexts texts)
{
    [McpServerTool(
        Name = "list_duplicates",
        Title = "List duplicates",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LibraryDuplicateListResponse))]
    [Description(
        "Lists the groups of copies the library holds more than once, as the web app's Duplicates page does — one file "
        + "under several names, byte-identical files, the same work twice, and possible matches — with each copy's files "
        + "and sizes and how much space the extra copies take. Use sort Recoverable to see first the groups whose "
        + "removal would free the most space. Groups the owner dismissed are left out unless decision asks for them. Read "
        + "only: removing a copy is done on the web app's Duplicates page.")]
    public async Task<CallToolResult> ListDuplicatesAsync(
        ClaimsPrincipal user,
        [Description("Keep only one class: SameFile (names of one file, which take its room once), ByteIdentical (files "
            + "with the same bytes), SameWork (copies of one work by its ids) or Possible (one title and year, no id that "
            + "decides).")]
        DuplicateClass? duplicateClass = null,
        [Description("Keep only the groups the owner decided so: Confirmed or Dismissed. Left out, every group but the "
            + "dismissed ones.")]
        DuplicateMatchDecision? decision = null,
        [Description("Default orders by class, then by title; Recoverable puts first the groups whose removal would free "
            + "the most space, by their recoverable estimate, and last those with none: byte-identical files whose "
            + "estimate isn't sure, copies of one work and possible matches.")]
        DuplicateSort? sort = null,
        [Description("The page, from 1.")]
        int page = 1,
        [Description("Groups per page, 1 to 100.")]
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await duplicates.ListAsync(
            duplicateClass?.ToString(), decision?.ToString(), sort?.ToString(), page, pageSize, user.GetUserId(), cancellationToken);
        if (result.Status != LibraryDuplicateListStatus.Listed)
            return McpToolResults.Error(texts.Render(result.Error!));

        var list = result.List!;
        return results.Success(
            texts.Render(Message.Of(
                "duplicates.mcp.list.listed", ("count", list.TotalCount), ("page", list.Page), ("listed", list.Items.Count))),
            list);
    }
}
