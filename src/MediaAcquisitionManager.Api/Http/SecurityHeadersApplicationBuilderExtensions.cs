namespace MediaAcquisitionManager.Api.Http;

public static class SecurityHeadersApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="SecurityHeadersMiddleware"/>. Call it before <c>UseStaticFiles</c>, which ends
    /// the pipeline for a static file — a middleware after it would never see those responses.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
