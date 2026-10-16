using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Api.Services.Search;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Search;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp.Tools;

/// <summary>MAM-45 — <c>search_releases</c> and <c>list_search_releases</c>: the Search page's search, and its stored
/// releases read again without asking the sources (Vasyl's twelfth tool).</summary>
/// <remarks>
/// <para>
/// The Search page's stored search (<see cref="ISearchService"/>), not the plain release search of
/// <c>POST /api/releases/search</c>: that one stores its releases under no search, so a send would find none of them
/// among the caller's and answer that the release is gone. A release these tools list is one a Read and download token
/// of the same user can send, as the Download dialog sends what the Search page lists.
/// </para>
/// <para>
/// The service decides what the caller reads — whose search it is, a day's lifetime, a failed source's sentence for the
/// owner alone — and answers its own sentences, which go back as they are. It takes its values as text, as a query
/// string carries them; the tools take typed ones, so the schema says what fits, and pass them on by name. Built per
/// call by the SDK, with the request's services.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class ReleaseTools(ISearchService searches, McpToolResults results)
{
    /// <summary>The page <c>search_releases</c> answers with; <c>list_search_releases</c> reads the others.</summary>
    private const int FirstPage = 1;

    [McpServerTool(
        Name = "search_releases",
        Title = "Search for releases",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ReleaseSearchToolResult))]
    [Description(
        "Asks every enabled release source (torrent tracker) for releases of a title, as the web app's Search page does, "
        + "and returns the search — its id, how each source answered, the library works it matched — with the first page "
        + "of its releases: each with its id, size in bytes, seeders, what its title claims (resolution, audio "
        + "languages) and the library copies it matches. It can take a minute. The search is kept for a day: to filter, "
        + "sort or page its releases afterwards, call list_search_releases with search.id instead of searching again. "
        + "Give the year, the IMDb or TMDb id or the season when known: they narrow what is matched. A release's id is "
        + "the releaseId get_download_options and send_download take.")]
    public async Task<CallToolResult> SearchReleasesAsync(
        ClaimsPrincipal user,
        [Description("The title to search for, as a release would name it; at most 200 characters.")]
        string text,
        [Description("The year it came out, such as 2014.")]
        int? year = null,
        [Description("Its IMDb id, such as tt0816692.")]
        string? imdbId = null,
        [Description("Its TMDb id, such as 157336 (list_recommendations gives one as searchTmdbId).")]
        string? tmdbId = null,
        [Description("A season of a series, such as 1 (0 for the specials); searched for in TV releases only.")]
        int? season = null,
        [Description("Keep only Movie or only Tv releases, by the category the source lists them in.")]
        ReleaseKind? kind = null,
        [Description("Keep only the releases whose title claims this audio language: a three-letter ISO 639-2 code such "
            + "as ukr or eng, or unknown for those that claim none.")]
        string? language = null,
        [Description("Keep only the releases whose title claims this resolution, such as 2160, 1080 or 720, or unknown "
            + "for those that claim none.")]
        string? quality = null,
        [Description("The smallest size to keep, in bytes.")]
        long? minSizeBytes = null,
        [Description("The largest size to keep, in bytes.")]
        long? maxSizeBytes = null,
        [Description("The order: Seeders (the default; the one that downloads soonest first), Largest, Smallest, Newest "
            + "(the most recently published first) or Quality (the highest resolution first).")]
        SearchReleaseSort? sort = null,
        [Description("Releases on this first page, 1 to 100; list_search_releases reads the next pages.")]
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // The list's values are checked before any source is asked, by the rules the list itself applies: a page size or
        // a filter it won't take would otherwise be refused only after a minute of searching, the search stored for
        // nothing. The search's own values are the service's to check, with nothing stored and no source asked.
        if ((PagingRules.Validate(FirstPage, pageSize)
                ?? SearchReleaseListRules.Validate(null, Name(kind), language, quality, Text(minSizeBytes), Text(maxSizeBytes), Name(sort), out _))
            is { } listError)
        {
            return McpToolResults.Error(listError);
        }

        var userId = user.GetUserId();
        var created = await searches.CreateAsync(text, Text(year), imdbId, tmdbId, Text(season), userId, cancellationToken);
        if (created.Status != SearchCreateStatus.Created)
            return McpToolResults.Error(created.Error!);

        var search = created.Search!;
        var listed = await ListAsync(
            search.Id, null, kind, language, quality, minSizeBytes, maxSizeBytes, sort, FirstPage, pageSize, userId, cancellationToken);
        if (listed.Status != SearchReleaseListStatus.Listed)
            return Refusal(listed);

        var releases = listed.List!;
        var filtered = IsFiltered(null, kind, language, quality, minSizeBytes, maxSizeBytes);
        return results.Success(SearchSummary(search, releases, filtered), new ReleaseSearchToolResult(search, releases));
    }

    [McpServerTool(
        Name = "list_search_releases",
        Title = "List a search's releases",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SearchReleaseListResponse))]
    [Description(
        "Lists the releases one of your searches found, filtered, sorted and paged by MAM, without asking the release "
        + "sources again: for \"show more\", \"only Ukrainian audio\" or \"the smallest one in 1080p\". The searchId is the "
        + "search.id search_releases returned; a search is kept for a day, after which search_releases has to run again. "
        + "totalCount says how many releases pass the filters in all.")]
    public async Task<CallToolResult> ListSearchReleasesAsync(
        ClaimsPrincipal user,
        [Description("The search's id: search.id from search_releases.")]
        Guid searchId,
        [Description("Keep only the releases of one source: the id of one of search.sources (not its integrationId).")]
        Guid? sourceId = null,
        [Description("Keep only Movie or only Tv releases, by the category the source lists them in.")]
        ReleaseKind? kind = null,
        [Description("Keep only the releases whose title claims this audio language: a three-letter ISO 639-2 code such "
            + "as ukr or eng, or unknown for those that claim none.")]
        string? language = null,
        [Description("Keep only the releases whose title claims this resolution, such as 2160, 1080 or 720, or unknown "
            + "for those that claim none.")]
        string? quality = null,
        [Description("The smallest size to keep, in bytes.")]
        long? minSizeBytes = null,
        [Description("The largest size to keep, in bytes.")]
        long? maxSizeBytes = null,
        [Description("The order: Seeders (the default; the one that downloads soonest first), Largest, Smallest, Newest "
            + "(the most recently published first) or Quality (the highest resolution first).")]
        SearchReleaseSort? sort = null,
        [Description("The page, from 1.")]
        int page = 1,
        [Description("Releases per page, 1 to 100.")]
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var listed = await ListAsync(
            searchId, sourceId, kind, language, quality, minSizeBytes, maxSizeBytes, sort, page, pageSize, user.GetUserId(), cancellationToken);
        if (listed.Status != SearchReleaseListStatus.Listed)
            return Refusal(listed);

        var list = listed.List!;
        var held = IsFiltered(sourceId, kind, language, quality, minSizeBytes, maxSizeBytes)
            ? $"{Releases(list.TotalCount)} of this search pass the filters"
            : $"This search holds {Releases(list.TotalCount)}";
        return results.Success($"{held}; page {list.Page} lists {list.Items.Count}.", list);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>One page of the caller's search's releases, the typed values passed on as the list's query string
    /// carries them.</summary>
    private Task<SearchReleaseListResult> ListAsync(
        Guid searchId,
        Guid? sourceId,
        ReleaseKind? kind,
        string? language,
        string? quality,
        long? minSizeBytes,
        long? maxSizeBytes,
        SearchReleaseSort? sort,
        int page,
        int pageSize,
        Guid userId,
        CancellationToken cancellationToken) =>
        searches.ListReleasesAsync(
            searchId,
            sourceId?.ToString(),
            Name(kind),
            language,
            quality,
            Text(minSizeBytes),
            Text(maxSizeBytes),
            Name(sort),
            page,
            pageSize,
            userId,
            cancellationToken);

    /// <summary>
    /// A list the service didn't serve: a search that isn't the caller's, is more than a day old or never was gets the one
    /// sentence the HTTP API's bare 404 stands for, so none confirms that the search exists; a value the list doesn't
    /// take gets the service's own sentence.
    /// </summary>
    private static CallToolResult Refusal(SearchReleaseListResult listed) =>
        McpToolResults.Error(listed.Status == SearchReleaseListStatus.Invalid ? listed.Error! : McpToolErrors.SearchNotFound);

    /// <summary>
    /// What the search found, in a sentence: how many releases in how many sources, how many pass the filters, how many
    /// the page lists, each source that failed or wasn't asked by name and status — why is the owner's sentence, in the
    /// data — and how many of the library's works it matched; those listed only for holding the title's words don't
    /// count (ADR 0012: not a match).
    /// </summary>
    private static string SearchSummary(SearchResponse search, SearchReleaseListResponse releases, bool filtered)
    {
        var found = search.Sources.Sum(source => source.ReleaseCount);
        var summary = $"Found {Releases(found)} for “{search.Text}” in {Count(search.Sources.Count, "release source")}";
        if (filtered)
            summary += $", {releases.TotalCount} of them passing the filters";
        summary += $"; the first page lists {releases.Items.Count}";

        var unanswered = search.Sources
            .Where(source => source.Status != IntegrationCallStatus.Succeeded)
            .Select(source => source.NotSearchedReason is { } reason
                ? $"{source.Name} was not searched ({reason})"
                : $"{source.Name} failed ({source.Status})")
            .ToList();
        if (unanswered.Count > 0)
            summary += "; " + string.Join(", ", unanswered);

        var matches = search.Library.Count(match => match.Evidence != MatchEvidence.Words);
        var library = matches switch
        {
            0 => "no matching work",
            1 => "1 matching work",
            _ => $"{matches} matching works",
        };
        return $"{summary}; the library holds {library}.";
    }

    /// <summary>Whether a filter narrows the list, so its count is of what passes rather than of the whole search; the
    /// order alone narrows nothing.</summary>
    private static bool IsFiltered(
        Guid? sourceId, ReleaseKind? kind, string? language, string? quality, long? minSizeBytes, long? maxSizeBytes) =>
        sourceId is not null
        || kind is not null
        || !string.IsNullOrWhiteSpace(language)
        || !string.IsNullOrWhiteSpace(quality)
        || minSizeBytes is not null
        || maxSizeBytes is not null;

    /// <summary>An enum by its name, which the list's rules read in any letter case.</summary>
    private static string? Name<TEnum>(TEnum? value)
        where TEnum : struct, Enum => value?.ToString();

    /// <summary>A number as the service's text, in the invariant culture: a negative one keeps its sign, so the rules'
    /// sentence quotes it as it was asked.</summary>
    private static string? Text(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string Releases(int count) => Count(count, "release");

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
