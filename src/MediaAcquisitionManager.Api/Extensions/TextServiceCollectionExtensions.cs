using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Options;

namespace MediaAcquisitionManager.Api.Extensions;

public static class TextServiceCollectionExtensions
{
    /// <summary>MAM-47 (ADR 0042) — the server's sentences in the request's language, and the startup step that converts
    /// the English older builds stored.</summary>
    public static IServiceCollection AddServerTexts(this IServiceCollection services, IConfiguration configuration)
    {
        // The request's language reaches the singleton mappers through the accessor (Ambient Context), so no service
        // lifetime changes; nothing registered it before.
        services.AddHttpContextAccessor();
        services.AddSectionOptions<TextOptions>(configuration);
        // Singleton: stateless apart from its once-per-key Warning memory, and every mapper is a singleton.
        services.AddSingleton<IRequestTexts, RequestTexts>();
        // Scoped: it holds the DbContext; resolved once per start, in the migration scope.
        services.AddScoped<StoredTextConverter>();
        return services;
    }
}
