using MediaAcquisitionManager.Api.Services.LibraryLinks;

namespace MediaAcquisitionManager.Api.Extensions;

public static class LibraryLinkServiceCollectionExtensions
{
    /// <summary>MAM-34 — a library work's TMDb id set by hand: the links, how every reader of a work's ids counts
    /// them, how a sync carries them onto items that came back under new ids, how they are set, removed and
    /// identified in the libraries, and the TMDb suggestions prepared for the works without an id.</summary>
    public static IServiceCollection AddLibraryLinkFeatures(this IServiceCollection services)
    {
        // Scoped: each works on its scope's DbContext — a request's, the search's own library scope, or the one a
        // worker opens per run.
        services.AddScoped<ILibraryLinkReader, LibraryLinkReader>();
        services.AddScoped<ILibraryLinkReattacher, LibraryLinkReattacher>();
        services.AddScoped<ILibraryLinkService, LibraryLinkService>();
        services.AddScoped<ILibraryLinkWriter, LibraryLinkWriter>();
        services.AddScoped<ILibraryLinkSuggestionReader, LibraryLinkSuggestionReader>();
        services.AddScoped<ILibraryLinkSuggestionService, LibraryLinkSuggestionService>();
        services.AddScoped<ILibraryLinkSuggestionRunService, LibraryLinkSuggestionRunService>();
        services.AddScoped<ILibraryLinkSuggestionRunner, LibraryLinkSuggestionRunner>();
        // Singleton: the one signal the request that queues links and the background writer share (R13).
        services.AddSingleton<LibraryLinkWriteQueue>();
        // Singleton: the suggestion runs' queue the triggers and the worker share (Queue/Worker).
        services.AddSingleton<LibraryLinkSuggestionRunQueue>();
        return services;
    }
}
