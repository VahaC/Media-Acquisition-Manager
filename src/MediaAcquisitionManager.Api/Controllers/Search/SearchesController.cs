using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Search;

/// <summary>The search page's searches (MAM-12): the library and every release source, asked once and
/// stored.</summary>
/// <remarks>
/// For every signed-in user, as the release search is (MAM-10's choice): a search changes nothing the
/// household owns. Outside <c>Controllers/Library</c> and <c>Controllers/Settings</c> on purpose: both
/// make every write owner-only, and this POST is a search. A search is its creator's: another user's, and
/// one more than a day old, answer the same bare 404 as one that never was — nothing confirms it exists.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/searches")]
public class SearchesController(ISearchService searches) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>
    /// Asks the library and every enabled release source at once and stores what they answered. 201
    /// whatever the sources answered — each says how it went, so a failing one never hides the others;
    /// 400 only for a search the app doesn't take, with nothing stored. A POST: every release found is
    /// stored, so a refetch must not repeat it. MAM-5 — open to every personal access token, as a read: a token has to
    /// search before it can send (ADR 0040).
    /// </summary>
    [HttpPost]
    [PersonalAccessTokenRead]
    public async Task<ActionResult<SearchResponse>> Create(
        // An empty body reaches the rules' "enter the title" sentence instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SearchRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await searches.CreateAsync(
            request?.Text, request?.Year, request?.ImdbId, request?.TmdbId, request?.Season, UserId, cancellationToken);
        return result.Status switch
        {
            SearchCreateStatus.Created => CreatedAtAction(nameof(Get), new { id = result.Search!.Id }, result.Search),
            _ => this.BadRequestError(result.Error),
        };
    }

    /// <summary>The search again — after a reload, say: the library matched now, the sources as stored.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SearchResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await searches.GetAsync(id, UserId, cancellationToken);
        return result.Status switch
        {
            SearchReadStatus.Found => Ok(result.Search),
            _ => NotFound(),
        };
    }

    /// <summary>One page of the search's releases, filtered and ordered by the server from what the search
    /// stored: no source is asked again.</summary>
    [HttpGet("{id:guid}/releases")]
    public async Task<ActionResult<SearchReleaseListResponse>> Releases(
        Guid id, [FromQuery] SearchReleaseListRequest request, CancellationToken cancellationToken)
    {
        var result = await searches.ListReleasesAsync(
            id,
            request.Source,
            request.Kind,
            request.Language,
            request.Quality,
            request.MinSizeBytes,
            request.MaxSizeBytes,
            request.Sort,
            request.Page,
            request.PageSize,
            UserId,
            cancellationToken);
        return result.Status switch
        {
            SearchReleaseListStatus.Listed => Ok(result.List),
            SearchReleaseListStatus.Invalid => this.BadRequestError(result.Error),
            _ => NotFound(),
        };
    }
}
