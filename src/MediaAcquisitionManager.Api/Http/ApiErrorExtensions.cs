using MediaAcquisitionManager.Core.Texts;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Http;

// MAM-47 (ADR 0042) — every helper takes a message and answers { "error": "<the request's language>", "code": "<key>" }.
// There is no string overload: the compiler proves every controller error carries a key, so no English sentence can
// reach a body by a call left behind.
public static class ApiErrorExtensions
{
    public static ObjectResult Error(this ControllerBase controller, int statusCode, Message message)
        => controller.StatusCode(statusCode, ApiErrorBody.For(controller.HttpContext, message));

    public static ObjectResult BadRequestError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status400BadRequest, message);

    public static ObjectResult UnauthorizedError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status401Unauthorized, message);

    public static ObjectResult ForbiddenError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status403Forbidden, message);

    public static ObjectResult NotFoundError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status404NotFound, message);

    public static ObjectResult ConflictError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status409Conflict, message);

    public static ObjectResult UnprocessableError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status422UnprocessableEntity, message);

    public static ObjectResult LockedError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status423Locked, message);

    public static ObjectResult ServerError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status500InternalServerError, message);

    public static ObjectResult BadGatewayError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status502BadGateway, message);

    public static ObjectResult UnavailableError(this ControllerBase controller, Message message)
        => controller.Error(StatusCodes.Status503ServiceUnavailable, message);
}
