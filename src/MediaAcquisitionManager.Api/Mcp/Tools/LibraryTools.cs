using System.ComponentModel;
using System.Security.Claims;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Enums;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp.Tools;

/// <summary>MAM-45 — <c>search_library</c>: what the media servers already hold, as the Library page lists it.</summary>
/// <remarks>
/// The Library page's own list (<see cref="ILibraryService.ListWorksAsync"/>), not the Search page's library matching:
/// that runs only inside a release search, which asks every source and stores what they found — slow, and a write, for
/// "do we have it?". The service shapes what the owner alone reads (a file's path problem quotes the owner's mappings)
/// and answers its own sentences, which go back as they are. Built per call by the SDK, with the request's services.
/// </remarks>
[McpServerToolType]
public sealed class LibraryTools(ILibraryService library, McpToolResults results)
{
    [McpServerTool(
        Name = "search_library",
        Title = "Search the library",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LibraryWorkListResponse))]
    [Description(
        "Searches the library — what the household's media servers already hold — and returns the matching works "
        + "(movies, series and episodes) with their copies: each version a server holds, its files, their sizes and "
        + "where they live. Use it before search_releases, to tell the user whether a copy is already there and what it "
        + "is like. A series is returned when one of its episodes matches. Results are paged: totalCount says how many "
        + "match in all, so ask for the next page rather than assuming the first one is everything.")]
    public async Task<CallToolResult> SearchLibraryAsync(
        ClaimsPrincipal user,
        [Description("Words of a title, a year such as 2021, an IMDb id (tt0816692), a TMDb id or part of a file path; "
            + "leave it out to list the whole library in the chosen order.")]
        string? text = null,
        [Description("Movie keeps the movies; Series keeps the series and the episodes that belong to none. Leave it out "
            + "for both.")]
        string? kind = null,
        [Description("The order: Title (the default) or TitleDescending; Newest or Oldest by year; Largest, the most on "
            + "disk first; Added, the most recently added to the library first.")]
        LibrarySort? sort = null,
        [Description("The page, from 1.")]
        int page = 1,
        [Description("Works per page, 1 to 100.")]
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        // The page's other two filters stay off: a library's id comes only from the page's own list of libraries, and
        // the works missing an id are fixed in the web app (Identify), which no tool reaches.
        var listed = await library.ListWorksAsync(
            text, locationId: null, identifiers: null, kind, sort?.ToString(), page, pageSize, user.GetUserId(), cancellationToken);
        if (listed.Status != LibraryWorkListStatus.Listed)
            return McpToolResults.Error(listed.Error!);

        var list = listed.List!;
        var matched = string.IsNullOrWhiteSpace(text)
            ? $"The library lists {Works(list.TotalCount)}"
            : list.TotalCount == 1
                ? $"1 work matches “{text.Trim()}”"
                : $"{list.TotalCount} works match “{text.Trim()}”";
        return results.Success($"{matched}; page {list.Page} lists {list.Items.Count}.", list);
    }

    private static string Works(int count) => count == 1 ? "1 work" : $"{count} works";
}
