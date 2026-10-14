using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

// MAM-30 — the download client contract's destinations for a client without categories (ADR 0039): Transmission's own
// download folder and each folder the app proposes, every one confirmed by free-space or listed with why Transmission can't
// use it — never hidden, never created. Every request is session-get or free-space.
internal sealed partial class TransmissionAdapter
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>session-get</c> first (<see cref="OpenAsync"/>), whose <c>download-dir</c> is the client's default folder; then
    /// one <c>free-space</c> of each distinct folder — the default and each of <paramref name="proposedFolders"/> that
    /// isn't the same folder (<see cref="NormalizedPath.IsSameAs"/>), merged into the default's entry when it is. A folder
    /// Transmission answers <c>success</c> with free bytes for is one it sees; any other answer is listed with
    /// <see cref="ClientDestination.Problem"/> — a folder that doesn't exist and one not mounted into Transmission answer
    /// alike (MAM-29). Only the free bytes are read, never the total, which Transmission spells two ways. Free space proves
    /// where to send, not that a write will work: Transmission creates a missing <c>download-dir</c> on its first write
    /// inside its own container, which is why a folder it doesn't see is never offered.
    /// </para>
    /// <para>
    /// Each entry's key, display name and folder are the folder's canonical form, as the app stores it (D1). The default
    /// folder that isn't a path the app can use — not absolute, or starting with exactly <c>//</c>, which POSIX leaves to
    /// the system to read — is listed with why and never sent to; so is one longer than a destination's key can hold.
    /// </para>
    /// <para>
    /// A proposed folder that isn't an absolute path is the caller's bug, thrown as <see cref="ArgumentException"/> before
    /// anything is sent: the app proposes only folders that parse (D3).
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<ClientDestination>>> ListDestinationsAsync(
        IntegrationConnectionSettings settings, IReadOnlyCollection<string> proposedFolders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(proposedFolders);
        var proposed = new List<NormalizedPath>();
        foreach (var folder in proposedFolders)
        {
            if (!NormalizedPath.TryParse(folder, out var parsed, out _))
                throw new ArgumentException("Each proposed folder must be an absolute path.", nameof(proposedFolders));
            if (!proposed.Exists(kept => kept.IsSameAs(parsed)))
                proposed.Add(parsed);
        }

        return CallAsync<IReadOnlyList<ClientDestination>>(
            settings, (client, sessionSecrets, token) => ReadDestinationsAsync(client, settings, proposed, sessionSecrets, token), cancellationToken);
    }

    private async Task<IntegrationCallResult<IReadOnlyList<ClientDestination>>> ReadDestinationsAsync(
        HttpClient client,
        IntegrationConnectionSettings settings,
        IReadOnlyList<NormalizedPath> proposed,
        ICollection<string> sessionSecrets,
        CancellationToken cancellationToken)
    {
        var call = NewCall(settings, isTest: false, sessionSecrets);
        var (session, failure) = await OpenAsync(client, call, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<IReadOnlyList<ClientDestination>>(failure);

        var destinations = new List<ClientDestination>();
        var folders = new List<(NormalizedPath Folder, bool IsDefault)>();
        var downloadDir = session!.DownloadDir;
        if (UsableFolder(downloadDir) is { } defaultFolder)
        {
            folders.Add((defaultFolder, true));
        }
        else
        {
            var shown = RemoteText.Clean(downloadDir, FolderMaximumLength, CallSecrets(settings, sessionSecrets));
            destinations.Add(new ClientDestination(
                downloadDir,
                shown,
                downloadDir,
                IsClientDefaultFolder: true,
                Problem: $"Transmission's download folder “{shown}” isn't a full path the app can use. In Transmission's web interface open Edit Preferences, Torrents, give “Download to” a full path such as /downloads, then look again."));
        }

        folders.AddRange(proposed.Where(folder => !folders.Exists(listed => listed.Folder.IsSameAs(folder))).Select(folder => (folder, false)));
        foreach (var (folder, isDefault) in folders)
        {
            var key = folder.ToString();
            string? problem;
            if (key.Length > DestinationRules.ClientDestinationKeyMaximumLength)
            {
                // D1 — a destination's key holds the folder, and no more than this: listed with why, never sent to.
                problem = $"{(isDefault ? "Transmission's download folder" : "The folder")} “{key}” is longer than the {DestinationRules.ClientDestinationKeyMaximumLength} characters a destination can hold.";
            }
            else
            {
                var (seen, readFailure) = await FolderProblemAsync(client, call, key, sessionSecrets, cancellationToken);
                if (readFailure is not null)
                    return Failed<IReadOnlyList<ClientDestination>>(readFailure);
                problem = seen;
            }

            destinations.Add(new ClientDestination(
                key, key, key, isDefault, problem is null ? null : SecretRedaction.Redact(problem, CallSecrets(settings, sessionSecrets))));
        }

        return IntegrationCallResult<IReadOnlyList<ClientDestination>>.Succeeded(destinations);
    }

    /// <summary>
    /// <c>free-space</c> of <paramref name="folder"/>: <c>null</c> when Transmission sees it, else the sentence saying it
    /// can't and how to fix it, with Transmission's own reason; or the failure of the whole call.
    /// </summary>
    private async Task<(string? Problem, ConnectionTestResult? Failure)> FolderProblemAsync(
        HttpClient client, RpcCall call, string folder, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var (answer, failure) = await SendRpcAsync(client, call, FreeSpaceMethod, new JsonObject { ["path"] = folder }, sessionSecrets, cancellationToken);
        if (failure is not null)
            return (null, failure);
        if (answer!.IsSuccess && Whole(answer.Arguments, "size-bytes") is >= 0)
            return (null, null);

        // A success without free bytes is as good as no answer: Transmission couldn't measure the folder.
        var reason = answer.IsSuccess ? "no free space reported" : RemoteText.Clean(answer.Result, ResultMaximumLength, CallSecrets(call.Settings, sessionSecrets));
        return (
            $"Transmission can't see “{folder}” ({reason}). Mount that folder into Transmission, or add a path mapping on this integration that leads there, then look again.",
            null);
    }

    /// <summary>
    /// The default folder as a path the app can send and store, or <c>null</c>: one that doesn't parse as an absolute
    /// path, or that starts with exactly two slashes — a path POSIX lets the system read its own way, which the app would
    /// otherwise store as another folder.
    /// </summary>
    private static NormalizedPath? UsableFolder(string text) =>
        (text.StartsWith("//", StringComparison.Ordinal) && !text.StartsWith("///", StringComparison.Ordinal))
        || !NormalizedPath.TryParse(text, out var folder, out _)
            ? null
            : folder;
}
