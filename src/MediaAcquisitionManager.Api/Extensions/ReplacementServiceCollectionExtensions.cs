using MediaAcquisitionManager.Api.Services.Replacements;

namespace MediaAcquisitionManager.Api.Extensions;

public static class ReplacementServiceCollectionExtensions
{
    /// <summary>
    /// MAM-24 — replacing old copies (ADR 0030–0034): the owner's gates, read by the Media folders page, the Download
    /// dialog and the send; the dialog's read; the send's plan; and the following of each scheduled replacement. MAM-25 —
    /// and the check of each replacement's new files once its download is done (ADR 0035). MAM-26 — and the move of the
    /// old copies into the quarantine, the library's notice, and the owner's Retry, Restore and Delete for good (ADR
    /// 0030). Its own extension: it reads the settings, the library and the downloads alike.
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
        // MAM-25 — scoped, as they read and write the pass's DbContext: the check of the new files, which
        // ReplacementVerificationTracker's passes call, and its two readers — the client's word and the disk, and the
        // library's — each its own service so the check reads as its steps.
        services.AddScoped<IReplacementNewFileReader, ReplacementNewFileReader>();
        services.AddScoped<IReplacementLibraryReader, ReplacementLibraryReader>();
        services.AddScoped<IReplacementVerifier, ReplacementVerifier>();
        // MAM-26 — a singleton: the replacements someone in this process is writing to the media for, which only memory
        // that dies with the process can tell apart from the ones a restart cut.
        services.AddSingleton<ReplacementJobsInHand>();
        // MAM-26 — scoped, as they read and write the pass's (or the request's) DbContext: the move into the quarantine,
        // which ReplacementQuarantineTracker's passes call; the library's notice after a move or a restore; and the
        // owner's own actions on a replacement.
        services.AddScoped<IReplacementQuarantiner, ReplacementQuarantiner>();
        services.AddScoped<IReplacementLibraryNotice, ReplacementLibraryNotice>();
        services.AddScoped<IReplacementJobActions, ReplacementJobActions>();
        return services;
    }
}
