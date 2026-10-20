using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Recommendations;

/// <summary>MAM-33 — the recommendations: each user's lists, their "Not interested" marks, the works' posters and
/// descriptions, and the computation runs.</summary>
/// <remarks>
/// <para>
/// Recommendations are every signed-in user's, so this controller lives outside <c>Controllers/Settings</c> and
/// <c>Controllers/Library</c>, whose writes are the owner's by convention; the one owner-only write here, Recompute
/// now, declares it on the action. A mark is the caller's own, by the token: there is no one else's to address.
/// </para>
/// <para>
/// A request to compute answers 202 with the queued run at once (202 + run row + reconciler) — the one it queued,
/// or the one already waiting, which it joins — and the page polls <c>runs/latest</c>.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/recommendations")]
public class RecommendationsController(
    IRecommendationRunService runs,
    IRecommendationListService lists,
    IRecommendationDismissalService dismissals,
    IRecommendationPosterService posters,
    IRecommendationOverviewService overviews) : ControllerBase
{
    /// <summary>
    /// How long the browser may keep a poster without asking again: 30 days, <c>immutable</c> — a catalog's poster path
    /// names one image for good — and <c>private</c>, as the answer needs the user's token.
    /// </summary>
    private const string PosterCacheControl = "private, max-age=2592000, immutable";

    private Guid UserId => User.GetUserId();

    /// <summary>
    /// One page of one tab as the caller sees it, filtered and ordered by the server, with what its filters offer, whether
    /// a catalog is connected and the latest run; 400 for a value the list doesn't take, or a tab the caller has off.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<RecommendationListResponse>> List([FromQuery] RecommendationListRequest request, CancellationToken cancellationToken)
    {
        var result = await lists.ListAsync(request, UserId, cancellationToken);
        return result.Status switch
        {
            RecommendationListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error!),
        };
    }

    /// <summary>"Not interested": 204, also when the work was already marked; 400 for a kind or an id a mark doesn't
    /// take. A PUT: the mark is a state, and a repeat changes nothing. MAM-5 — open to a personal access token with Read
    /// and download access; its undo below is not (ADR 0040).</summary>
    [HttpPut("dismissals/{kind}/{catalogId}")]
    [PersonalAccessTokenWrite]
    public async Task<IActionResult> Dismiss(string kind, string catalogId, CancellationToken cancellationToken)
    {
        var result = await dismissals.DismissAsync(kind, catalogId, UserId, cancellationToken);
        return result.Status switch
        {
            RecommendationDismissalStatus.Done => NoContent(),
            _ => this.BadRequestError(result.Error!),
        };
    }

    /// <summary>Undo of "Not interested": 204, also when there was no mark; 400 as <see cref="Dismiss"/>.</summary>
    [HttpDelete("dismissals/{kind}/{catalogId}")]
    public async Task<IActionResult> Undismiss(string kind, string catalogId, CancellationToken cancellationToken)
    {
        var result = await dismissals.UndismissAsync(kind, catalogId, UserId, cancellationToken);
        return result.Status switch
        {
            RecommendationDismissalStatus.Done => NoContent(),
            _ => this.BadRequestError(result.Error!),
        };
    }

    /// <summary>
    /// A catalog work's poster, as the list's <c>posterUrl</c> names it: the image, from the app's cache or fetched from
    /// the catalog on the first request; a bare 404 for a name that is no catalog work's poster, as for a missing row;
    /// 502 when the catalog couldn't send it. Signed in: the SPA fetches it with the token and shows it as a <c>data:</c>
    /// URL, so the CSP stays <c>img-src 'self' data:</c> and no endpoint answers anonymously.
    /// </summary>
    [HttpGet("posters/{fileName}")]
    public async Task<IActionResult> Poster(string fileName, CancellationToken cancellationToken)
    {
        var result = await posters.GetAsync(fileName, UserId, cancellationToken);
        switch (result.Status)
        {
            case RecommendationPosterStatus.Served:
                Response.Headers.CacheControl = PosterCacheControl;
                return File(result.Content!, result.MediaType!);
            case RecommendationPosterStatus.NotFound:
                return NotFound();
            default:
                return this.BadGatewayError(result.Error!);
        }
    }

    /// <summary>
    /// What a catalog work is about, for its card's Description dialog: in <paramref name="language"/> (English when
    /// none is given), else in English, else none — kept, or asked of the catalog on the first request. 400 for a kind,
    /// an id or a language the request doesn't take; a bare 404 for a work the catalog doesn't have, as for a missing
    /// row; 409 when nothing is kept and no catalog is connected; 502 when the catalog couldn't answer.
    /// </summary>
    [HttpGet("overviews/{kind}/{catalogId}")]
    public async Task<ActionResult<RecommendationOverviewResponse>> Overview(
        string kind, string catalogId, [FromQuery] string? language, CancellationToken cancellationToken)
    {
        var result = await overviews.GetAsync(kind, catalogId, language, UserId, cancellationToken);
        return result.Status switch
        {
            RecommendationOverviewStatus.Found => Ok(result.Overview),
            RecommendationOverviewStatus.NotFound => NotFound(),
            RecommendationOverviewStatus.Invalid => this.BadRequestError(result.Error!),
            // Nothing to ask: a state the owner changes under Integrations, not a bad request.
            RecommendationOverviewStatus.NoCatalog => this.ConflictError(result.Error!),
            _ => this.BadGatewayError(result.Error!),
        };
    }

    /// <summary>The run under way, else the latest one; 204 before the first. Its requester and error for the owner
    /// only.</summary>
    [HttpGet("runs/latest")]
    public async Task<ActionResult<RecommendationRunDto>> LatestRun(CancellationToken cancellationToken) =>
        await runs.GetLatestAsync(UserId, cancellationToken) is { } run ? Ok(run) : NoContent();

    /// <summary>The owner's Recompute now: queues a computation, or joins the one waiting.</summary>
    [HttpPost("runs")]
    [RequireInstallationOwner]
    public async Task<ActionResult<RecommendationRunDto>> StartRun(CancellationToken cancellationToken)
    {
        var result = await runs.StartAsync(UserId, cancellationToken);
        return result.Status switch
        {
            // Named: with a bare null the (action, controller, route values) overload wins, and the run would go out as
            // route values behind an empty body.
            RecommendationRunStartStatus.Queued => AcceptedAtAction(nameof(LatestRun), routeValues: null, value: result.Run),
            // Nothing to compute from: a state the owner changes under Integrations, not a bad request.
            RecommendationRunStartStatus.NoCatalog => this.ConflictError(result.Error!),
            _ => this.UnavailableError(result.Error!),
        };
    }
}
