using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MediaAcquisitionManager.Api.Http;

/// <summary>
/// MAM-33 — answers a request body MVC couldn't read — not JSON, or a field of the wrong type — with
/// <c>{ "error": <see cref="Error"/> }</c> and 400, instead of the ValidationProblemDetails body <c>[ApiController]</c>
/// would send: a second error format the SPA can't show.
/// </summary>
/// <remarks>
/// <para>
/// A missing or empty body is the action's to judge: with nullable fields and
/// <c>[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]</c> it reaches the service, whose sentence names the field.
/// What reaches this filter is a body the formatter couldn't turn into the request at all.
/// </para>
/// <para>
/// Runs before <c>[ApiController]</c>'s model-state filter (order -2000), which would otherwise answer first; only for
/// the actions that carry it, so every other action keeps its behaviour.
/// </para>
/// </remarks>
/// <param name="error">The sentence the caller gets: what the body must be.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UnreadableBodyErrorAttribute(string error) : Attribute, IActionFilter, IOrderedFilter
{
    /// <summary>The sentence the caller gets.</summary>
    public string Error { get; } = error;

    /// <summary>Before <c>ModelStateInvalidFilter</c>'s -2000.</summary>
    public int Order => -3000;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid && context.Controller is ControllerBase controller)
            context.Result = controller.BadRequestError(Error);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
