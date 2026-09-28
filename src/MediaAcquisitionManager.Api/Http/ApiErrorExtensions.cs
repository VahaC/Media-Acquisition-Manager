using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Http;

public static class ApiErrorExtensions
{
    public static ObjectResult Error(this ControllerBase controller, int statusCode, string? message)
        => controller.StatusCode(statusCode, new { error = message });

    public static ObjectResult BadRequestError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status400BadRequest, message);

    public static ObjectResult UnauthorizedError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status401Unauthorized, message);

    public static ObjectResult ForbiddenError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status403Forbidden, message);

    public static ObjectResult NotFoundError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status404NotFound, message);

    public static ObjectResult ConflictError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status409Conflict, message);

    public static ObjectResult UnprocessableError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status422UnprocessableEntity, message);

    public static ObjectResult LockedError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status423Locked, message);

    public static ObjectResult ServerError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status500InternalServerError, message);

    public static ObjectResult BadGatewayError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status502BadGateway, message);

    public static ObjectResult UnavailableError(this ControllerBase controller, string? message)
        => controller.Error(StatusCodes.Status503ServiceUnavailable, message);
}
