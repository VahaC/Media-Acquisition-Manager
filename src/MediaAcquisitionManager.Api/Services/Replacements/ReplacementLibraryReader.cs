using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementLibraryReader"/>
/// <remarks>
/// <para>
/// In this order: the integration must still be there, of a type with the capability, switched on and with a secret the
/// app can open; then the version gate; then the call, in batches of at most
/// <see cref="IMediaLibraryFileLookup.MaximumPaths"/> paths. A type without the capability is checked before the switch:
/// switching the library on would never let it confirm a file, so the owner hears at once. Each failure's sentence names
/// the library by its name now, as Integrations lists it, and quotes the library's own sentence, masked again after the
/// adapter (ADR 0007's second pass).
/// </para>
/// <para>
/// MAM-26 — the notice after a move or a restore (<see cref="TellAsync"/>) goes through the same gates, batches and
/// masking as asking; only its reasons are its own, as it tells of old copies rather than asking about new files.
/// </para>
/// <para>
/// Information: an integration's id, its type and a call's status, never a path, an address or a sentence.
/// </para>
/// </remarks>
public sealed class ReplacementLibraryReader(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    ILogger<ReplacementLibraryReader> logger) : IReplacementLibraryReader
{
    private const string RemovedReason = "The library that holds the new files was removed from Integrations, so they can't be confirmed.";

    private const string RemovedNoticeReason =
        "The library the old copies were in was removed from Integrations, so it can't be told that they moved.";

    public Task<ReplacementLibraryAnswer> AskAsync(Guid integrationId, IReadOnlyList<string> libraryPaths, CancellationToken cancellationToken = default) =>
        CallAsync(integrationId, libraryPaths, Purpose.Ask, LibraryFileChange.Created, cancellationToken);

    public Task<ReplacementLibraryAnswer> ReadAsync(Guid integrationId, IReadOnlyList<string> libraryPaths, CancellationToken cancellationToken = default) =>
        CallAsync(integrationId, libraryPaths, Purpose.Read, change: null, cancellationToken);

    public Task<ReplacementLibraryAnswer> TellAsync(
        Guid integrationId, IReadOnlyList<string> libraryPaths, LibraryFileChange change, CancellationToken cancellationToken = default) =>
        CallAsync(integrationId, libraryPaths, Purpose.Tell, change, cancellationToken);

    /// <summary>
    /// One call for <paramref name="purpose"/>: a request to look, with <paramref name="change"/> as what happened at the
    /// paths, or — <paramref name="change"/> <c>null</c> — a read of them.
    /// </summary>
    private async Task<ReplacementLibraryAnswer> CallAsync(
        Guid integrationId, IReadOnlyList<string> libraryPaths, Purpose purpose, LibraryFileChange? change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(libraryPaths);

        // Removed: its folders went with it, so the files are in no library it can speak for any more.
        if (await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == integrationId, cancellationToken) is not { } integration)
            return Answer(ReplacementCheckStatus.Review, integrationId.ToString(), purpose == Purpose.Tell ? RemovedNoticeReason : RemovedReason);

        var name = integration.Name;
        if (adapters.Find<IMediaLibraryFileLookup>(integration.AdapterType) is not { } lookup)
            return Answer(ReplacementCheckStatus.Review, name, NoCapabilityReason(integration.AdapterType, purpose));
        if (!integration.IsEnabled)
            return Answer(ReplacementCheckStatus.Wait, name, SwitchedOffReason(name, purpose));
        var built = settingsFactory.Create(integration);
        if (built.Settings is not { } settings)
            return Answer(ReplacementCheckStatus.Wait, name, built.Error!);

        var call = purpose switch
        {
            Purpose.Ask => "scan request",
            Purpose.Read => "files read",
            _ => "library notice",
        };
        try
        {
            // The version gate (MAM-8), as before every sync: a server the app can't read safely is asked nothing.
            var test = await adapters.Find(integration.AdapterType)!.TestConnectionAsync(settings, cancellationToken);
            if (!test.IsSuccess)
                return Failed(integration, "connection test", test.Status, purpose, Masked(test.Message, settings));

            // In batches of at most what one call takes: a whole series' pack, or the new files read with as many ticked
            // copies, can name more, which the adapter refuses as a caller's mistake. Asking again is harmless and reading
            // is only reading, so the readings are merged in the order asked, and a batch that fails fails the whole call,
            // as one call would.
            var readings = new List<LibraryFileReading>(change is null ? libraryPaths.Count : 0);
            foreach (var batch in libraryPaths.Chunk(IMediaLibraryFileLookup.MaximumPaths))
            {
                if (change is { } happened)
                {
                    var asked = await lookup.RequestScanAsync(settings, batch, happened, cancellationToken);
                    if (!asked.IsSuccess)
                        return Failed(integration, call, asked.Status, purpose, Masked(asked.Error!, settings));
                    continue;
                }

                var read = await lookup.ReadFilesAsync(settings, batch, cancellationToken);
                if (!read.IsSuccess)
                    return Failed(integration, call, read.Status, purpose, Masked(read.Error!, settings));
                readings.AddRange(read.Value!);
            }

            return new ReplacementLibraryAnswer(ReplacementCheckStatus.Pass, name, readings.AsReadOnly(), null);
        }
        catch (ArgumentException exception)
        {
            // The adapter refused the call before sending it, as a caller's mistake (IMediaLibraryFileLookup): the app's own
            // fault, never the library's, and one every pass would make again, so the owner hears it at once — not an hour
            // later as an answer the app couldn't read. Error, with the type and the stack, never the message.
            logger.LogError(
                "The {Call} for a replacement to integration {IntegrationId} ({AdapterType}) was refused as a caller's mistake: {ExceptionType} {StackTrace}",
                call,
                integration.Id,
                integration.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return Answer(ReplacementCheckStatus.Review, name, RefusedReason(name, purpose));
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // An adapter never throws on an expected failure; this is a bug meeting an answer nobody foresaw. It costs this
            // pass its answer, never the job: the next pass asks again. Error, with the type and the stack, never the
            // message: a parser's message can quote what the library sent.
            logger.LogError(
                "The {Call} for a replacement to integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                call,
                integration.Id,
                integration.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return Answer(ReplacementCheckStatus.Wait, name, ThrewReason(name, purpose));
        }
    }

    /// <summary>A call the library failed: a wait, worded for the wait it counts against, and a line in the log.</summary>
    private ReplacementLibraryAnswer Failed(
        IntegrationConnectionEntity integration, string call, IntegrationCallStatus status, Purpose purpose, string error)
    {
        logger.LogInformation(
            "The {Call} for a replacement to integration {IntegrationId} ({AdapterType}) failed: {Status}.",
            call,
            integration.Id,
            integration.AdapterType,
            status);
        return Answer(
            ReplacementCheckStatus.Wait,
            integration.Name,
            purpose switch
            {
                Purpose.Ask => NotAskedReason(integration.Name, error),
                Purpose.Read => NotReadReason(integration.Name, error),
                _ => NotToldReason(integration.Name, error),
            });
    }

    private static ReplacementLibraryAnswer Answer(ReplacementCheckStatus status, string libraryName, string? reason) =>
        new(status, libraryName, [], reason);

    private static string Masked(string error, IntegrationConnectionSettings settings) => SecretRedaction.Redact(error, settings.Secrets)!;

    // The reasons, worded as the job stops with them: a wait's once its hour ran out. MAM-26 — a notice's are worded for
    // what the library wasn't told: a notice never stops a job.

    private static string NoCapabilityReason(string adapterType, Purpose purpose) => purpose == Purpose.Tell
        ? $"This version of the app can't ask {ReplacementText.Quote(adapterType)} to look at a file, so it can't be told that the old copies moved."
        : $"This version of the app can't ask {ReplacementText.Quote(adapterType)} to look at a file, so the new files can't be confirmed.";

    private static string SwitchedOffReason(string libraryName, Purpose purpose) => purpose == Purpose.Tell
        ? $"{ReplacementText.Quote(libraryName)} is switched off, so it can't be told that the old copies moved."
        : $"{ReplacementText.Quote(libraryName)} is switched off, so it can't confirm the new files.";

    private static string ThrewReason(string libraryName, Purpose purpose) => purpose == Purpose.Tell
        ? $"The app couldn't read what {ReplacementText.Quote(libraryName)} answered, so it may not know that the old copies moved."
        : $"The app couldn't read what {ReplacementText.Quote(libraryName)} answered, so the new files can't be confirmed.";

    private static string RefusedReason(string libraryName, Purpose purpose) => purpose == Purpose.Tell
        ? $"The app refused its own request to {ReplacementText.Quote(libraryName)} about the old copies as one it should never make, "
            + "so it wasn't told that they moved."
        : $"The app refused its own request to {ReplacementText.Quote(libraryName)} about the new files as one it should never make, "
            + "so they can't be confirmed.";

    private static string NotAskedReason(string libraryName, string error) =>
        $"The app couldn't ask {ReplacementText.Quote(libraryName)} to look at the new files within an hour: {error}";

    private static string NotReadReason(string libraryName, string error) =>
        $"The app couldn't read {ReplacementText.Quote(libraryName)} within an hour of asking it to look at the new files: {error}";

    private static string NotToldReason(string libraryName, string error) =>
        $"The app couldn't tell {ReplacementText.Quote(libraryName)} that the old copies moved: {error}";

    /// <summary>
    /// MAM-26 — what a call is for, which words its reasons and its log lines (a <c>bool asking</c> while there were two):
    /// asking the library to look at the new files, reading what it holds at them, or telling it of old copies that went
    /// or came back.
    /// </summary>
    private enum Purpose
    {
        Ask = 1,
        Read = 2,
        Tell = 3,
    }
}
