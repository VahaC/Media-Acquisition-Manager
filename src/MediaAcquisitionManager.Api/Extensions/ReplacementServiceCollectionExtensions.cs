using MediaAcquisitionManager.Api.Services.Replacements;

namespace MediaAcquisitionManager.Api.Extensions;

public static class ReplacementServiceCollectionExtensions
{
    /// <summary>
    /// MAM-24 — replacing old copies (ADR 0030–0034): the owner's gates, read by the Media folders page, the Download
    /// dialog and the send; the dialog's read; the send's plan; and the following of each scheduled replacement. Its own
    /// extension: it reads the settings, the library and the downloads alike.
    /// </summary>
    public static IServiceCollection AddReplacementFeatures(this IServiceCollection services)
    {
        // Scoped: it reads the request's DbContext, fresh on every call.
        services.AddScoped<IReplacementGates, ReplacementGates>();
        // Scoped, as they read the request's DbContext: what a release offers to replace, from the database and the disk
        // (the dialog's read and the send's plan alike); which torrents hold those copies, on the dialog's word only;
        // and the dialog's read, which puts the two together.
        services.AddScoped<IReplacementCandidateReader, ReplacementCandidateReader>();
        services.AddScoped<IReplacementHoldingReader, ReplacementHoldingReader>();
        services.AddScoped<IReplacementOptionsReader, ReplacementOptionsReader>();
        // Scoped, as they read and write the request's (or the following pass's) DbContext: the send's plan, from the
        // database and the disk alone, and the following of each replacement's download, which the send and
        // DownloadJobTracker's passes both call.
        services.AddScoped<IReplacementPlanner, ReplacementPlanner>();
        services.AddScoped<IReplacementJobFollower, ReplacementJobFollower>();
        return services;
    }
}
