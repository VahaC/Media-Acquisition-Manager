using System.Text.Json.Serialization;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Serialization;

namespace MediaAcquisitionManager.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    /// <summary>Controllers with the house JSON shape, and the global <c>{ error }</c> exception handler.</summary>
    public static IServiceCollection AddMediaAcquisitionManagerApi(this IServiceCollection services)
    {
        services.AddControllers().AddJsonOptions(options =>
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
