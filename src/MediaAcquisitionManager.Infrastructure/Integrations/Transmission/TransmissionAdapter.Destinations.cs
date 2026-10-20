using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

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
            var shown = VendorText.Clean(downloadDir, FolderMaximumLength, CallSecrets(settings, sessionSecrets));
            destinations.Add(new ClientDestination(
                downloadDir,
                shown.Text,
                downloadDir,
                IsClientDefaultFolder: true,
                Problem: Message.Of("transmission.folder.download-folder-unusable", ("folder", shown))));
        }

        folders.AddRange(proposed.Where(folder => !folders.Exists(listed => listed.Folder.IsSameAs(folder))).Select(folder => (folder, false)));
        foreach (var (folder, isDefault) in folders)
        {
            var key = folder.ToString();
            Message? problem;
            if (key.Length > DestinationRules.ClientDestinationKeyMaximumLength)
            {
                // D1 — a destination's key holds the folder, and no more than this: listed with why, never sent to.
                (string, MessageValue) longFolder = ("folder", key);
                (string, MessageValue) limit = ("count", DestinationRules.ClientDestinationKeyMaximumLength);
                problem = isDefault
                    ? Message.Of("transmission.folder.download-folder-too-long", longFolder, limit)
                    : Message.Of("transmission.folder.too-long", longFolder, limit);
            }
            else
            {
                var (seen, readFailure) = await FolderProblemAsync(client, call, key, sessionSecrets, cancellationToken);
                if (readFailure is not null)
                    return Failed<IReadOnlyList<ClientDestination>>(readFailure);
                problem = seen;
            }

            // The second pass the finished sentence got before (the folder is the app's or Transmission's, unmasked so
            // far): every value masked again, now that the call's session secrets are all known.
            destinations.Add(new ClientDestination(key, key, key, isDefault, problem?.Redacted(CallSecrets(settings, sessionSecrets))));
        }

        return IntegrationCallResult<IReadOnlyList<ClientDestination>>.Succeeded(destinations);
    }

    /// <summary>
    /// MAM-47 — the sentence a folder client lists a folder it can't see with, naming the client
    /// (<paramref name="service"/>) and its own reason. The test host's folder client (<c>FakeFolderDownloadClient</c>)
    /// lists its folders with this very sentence, so a test of the app's side reads the words a reader of Transmission's
    /// would.
    /// </summary>
    internal static Message FolderNotSeen(string service, string folder, VendorText reason) =>
        Message.Of("transmission.folder.not-seen", ("service", service), ("folder", folder), ("reason", reason));

    /// <summary>
    /// <c>free-space</c> of <paramref name="folder"/>: <c>null</c> when Transmission sees it, else the sentence saying it
    /// can't and how to fix it, with Transmission's own reason; or the failure of the whole call.
    /// </summary>
    private async Task<(Message? Problem, ConnectionTestResult? Failure)> FolderProblemAsync(
        HttpClient client, RpcCall call, string folder, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var (answer, failure) = await SendRpcAsync(client, call, FreeSpaceMethod, new JsonObject { ["path"] = folder }, sessionSecrets, cancellationToken);
        if (failure is not null)
            return (null, failure);
        if (answer!.IsSuccess && Whole(answer.Arguments, "size-bytes") is >= 0)
            return (null, null);

        // A success without free bytes is as good as no answer: Transmission couldn't measure the folder. MAM-47 — said
        // in a sentence of its own: the reason is then the app's words, not Transmission's.
        return (
            answer.IsSuccess
                ? Message.Of("transmission.folder.not-seen-no-free-space", ("service", Descriptor.DisplayName), ("folder", folder))
                : FolderNotSeen(Descriptor.DisplayName, folder, VendorText.Clean(answer.Result, ResultMaximumLength, CallSecrets(call.Settings, sessionSecrets))),
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
