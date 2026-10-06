using System.Text.Json.Serialization;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Serialization;

namespace MediaAcquisitionManager.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    /// <summary>Controllers with the house JSON shape and the owner-only Settings and TMDb links conventions, and the
    /// global <c>{ error }</c> exception handler.</summary>
    public static IServiceCollection AddMediaAcquisitionManagerApi(this IServiceCollection services)
    {
        services.AddControllers(options =>
        {
            // MAM-4 — every write under Controllers/Settings is owner-only by where it lives, not by an
            // attribute each new controller has to remember.
            options.Conventions.Add(new SettingsWritesAreOwnerOnlyConvention());
            // MAM-34 — the same for every action under Controllers/Library/Links, its reads too (R19).
            options.Conventions.Add(new LibraryLinksAreOwnerOnlyConvention());
        })
        .ConfigureApiBehaviorOptions(options =>
            // MAM-4 — a bare NotFound() answers a 404 with no body, which is what "the same bare 404
            // for a missing row" means. Under [ApiController] it would otherwise grow a problem-details
            // body: a second error format beside { error }. A failure with something to say uses the
            // { error } helpers, which this doesn't touch.
            options.SuppressMapClientErrors = true)
        .AddJsonOptions(options =>
        {
            // Enums travel as their names, so a reordered enum never changes what a stored or
            // cached client value means.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            // Every DateTime leaves with a trailing Z; see UtcDateTimeConverter for the bug it prevents.
            options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        // Required, not a second error format: a parameterless UseExceptionHandler() refuses to
        // start unless a problem-details service exists, even with an IExceptionHandler registered.
        // GlobalExceptionHandler always handles the exception, so no problem-details body is written.
        services.AddProblemDetails();
        return services;
    }
}
