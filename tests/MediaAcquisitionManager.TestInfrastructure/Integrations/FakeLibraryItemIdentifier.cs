using System.Collections.Concurrent;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-34 — one more media library type, registered in the test host beside the real ones, that identifies items
/// (<see cref="IMediaLibraryItemIdentifier"/>) and answers each identification as a test arms it: the link writer and its
/// background worker are tested through it without a Jellyfin, so a test can make <see cref="IdentifyAsync"/> take any
/// outcome, fail with any status, hold a call open or throw.
/// </summary>
/// <remarks>
/// <para>
/// A library type of its own (<see cref="TypeId"/>, neither <c>jellyfin</c> nor <c>fake</c>): the real adapter's behaviour
/// is tested against the fake Jellyfin server, and the flows that sit above it against this. Its connection test succeeds
/// unless a test arms another answer, because the link writer runs it as the version gate before every identification.
/// The identification itself is unarmed until a test says what it answers: it then fails with
/// <see cref="IntegrationCallStatus.Unreachable"/>, so a test that forgot to arm it sees a failing library and can't pass for
/// a working one. MAM-35 — a poster likewise (<see cref="Posters"/>), so the poster service's failures and their masking are
/// tested without a Jellyfin.
/// </para>
/// <para>
/// Thread safe: the worker's lanes call it from several threads at once. The requests are recorded in a concurrent queue, and
/// <see cref="PeakIdentificationsInFlight"/> says how many were being answered at once.
/// </para>
/// <para>
/// MAM-47 (ADR 0042) — its own words (unarmed, its connection test, the kinds it keeps in the app, a refusal a test
/// hands it) are no sentence of the app's and have no key in the locale files: they travel as
/// <see cref="Message.Legacy"/>, which every language shows exactly as they are, so a test reads them in any pass. A
/// sentence the real adapters make comes from their keys; a test that needs one arms the fake with it.
/// </para>
/// </remarks>
public sealed class FakeLibraryItemIdentifier : IIntegrationAdapter, IMediaLibraryProvider, IMediaLibraryItemIdentifier
{
    /// <summary>The type id integrations of this fake library are stored with.</summary>
    public const string TypeId = "fake-library";

    /// <summary>The product name the fake describes itself with.</summary>
    public const string DisplayName = "Fake library";

    /// <summary>What an unarmed identification answers with.</summary>
    public const string UnarmedMessage = "The fake library was not armed.";

    private readonly ConcurrentQueue<LibraryIdentifyRequest> _requests = new();
    private readonly ConcurrentQueue<IntegrationConnectionSettings> _calls = new();
    private readonly object _flightGate = new();
    private int _inFlight;
    private int _peakInFlight;

    public IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        DisplayName,
        IntegrationKind.MediaLibrary,
        UserName: CredentialRequirement.Optional,
        Password: CredentialRequirement.Optional,
        ApiKey: CredentialRequirement.Optional,
        ReportsFilePaths: true);

    /// <inheritdoc/>
    public MediaLibraryCapabilities Capabilities { get; } = new(GroupsVersionsUnderOneItem: false);

    /// <summary>The kinds of item it identifies: films and series, as Jellyfin does. A test sets another list to prove a
    /// kind is kept in the app.</summary>
    public IReadOnlyList<MediaWorkKind> IdentifiableKinds { get; set; } = [MediaWorkKind.Movie, MediaWorkKind.Series];

    /// <summary>The answer to a connection test; a success when <c>null</c>.</summary>
    public Func<IntegrationConnectionSettings, ConnectionTestResult>? ConnectionTest { get; set; }

    /// <summary>What an identification is decided as, given the request: answered as a success. <c>null</c> leaves the
    /// answer to <see cref="Answer"/>, or the unarmed failure. May block, to hold the call open while a test looks at what
    /// else runs.</summary>
    public Func<LibraryIdentifyRequest, LibraryIdentification>? Identification { get; set; }

    /// <summary>The whole answer to an identification, given the request, a failure status included. Wins over
    /// <see cref="Identification"/> when both are set.</summary>
    public Func<LibraryIdentifyRequest, IntegrationCallResult<LibraryIdentification>>? Answer { get; set; }

    /// <summary>When set, every identification throws it, as a bug in an adapter would.</summary>
    public Exception? Throws { get; set; }

    /// <summary>The items a library list answers; unarmed, a list fails with <see cref="IntegrationCallStatus.Unreachable"/>.</summary>
    public Func<LibraryPageRequest, IntegrationCallResult<LibraryItemPage>>? ItemPages { get; set; }

    /// <summary>The libraries a location list answers; none when <c>null</c>.</summary>
    public IReadOnlyList<LibraryLocation>? Locations { get; set; }

    /// <summary>MAM-35 — the whole answer to a poster, given the item id and the tag asked for, a failure status included;
    /// unarmed, a poster fails with <see cref="IntegrationCallStatus.Unreachable"/>. May block, or throw as a bug in an
    /// adapter would.</summary>
    public Func<string, string, IntegrationCallResult<LibraryPoster>>? Posters { get; set; }

    /// <summary>Every identification asked of it since the last <see cref="Reset"/>, oldest first.</summary>
    public IReadOnlyList<LibraryIdentifyRequest> Requests => [.. _requests];

    /// <summary>The connection settings of every call since the last <see cref="Reset"/>, oldest first — the address and
    /// the secrets as the caller handed them, decrypted.</summary>
    public IReadOnlyList<IntegrationConnectionSettings> Calls => [.. _calls];

    /// <summary>The most identifications that were being answered at once since the last <see cref="Reset"/>.</summary>
    public int PeakIdentificationsInFlight
    {
        get
        {
            lock (_flightGate)
                return _peakInFlight;
        }
    }

    /// <summary>
    /// The identification a library gives a link that went well: the request's id Identified, with the item's ids before
    /// empty and after the id asked for, and Apply sent.
    /// </summary>
    public static LibraryIdentification Identified(LibraryIdentifyRequest request) =>
        new(LibraryIdentifyOutcome.Identified, null, Sent: true, [], [request.Identifier], null);

    /// <summary>A refusal the library gives before sending anything, with the sentence the owner reads.</summary>
    public static LibraryIdentification Refused(LibraryIdentifyRefusal refusal, string sentence) =>
        new(LibraryIdentifyOutcome.Refused, refusal, Sent: false, [], [], Message.Legacy(sentence));

    /// <summary>Forgets what it was asked and disarms everything: the answers, the kinds, the lists and the posters.</summary>
    public void Reset()
    {
        ConnectionTest = null;
        Identification = null;
        Answer = null;
        Throws = null;
        ItemPages = null;
        Locations = null;
        Posters = null;
        IdentifiableKinds = [MediaWorkKind.Movie, MediaWorkKind.Series];
        _requests.Clear();
        _calls.Clear();
        lock (_flightGate)
        {
            _inFlight = 0;
            _peakInFlight = 0;
        }
    }

    public Task<ConnectionTestResult> TestConnectionAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        _calls.Enqueue(settings);
        return Task.FromResult(ConnectionTest?.Invoke(settings) ?? ConnectionTestResult.Succeeded(Message.Legacy("Connected to the fake library.")));
    }

    /// <inheritdoc/>
    public Message UnidentifiableKindReason(MediaWorkKind kind) =>
        Message.Legacy($"The fake library doesn't identify {kind.ToString().ToLowerInvariant()} items, so a link on one stays in the app.");

    /// <inheritdoc/>
    public Task<IntegrationCallResult<LibraryIdentification>> IdentifyAsync(
        IntegrationConnectionSettings settings, LibraryIdentifyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _calls.Enqueue(settings);
        _requests.Enqueue(request);
        if (Throws is { } exception)
            throw exception;

        lock (_flightGate)
        {
            _inFlight++;
            _peakInFlight = Math.Max(_peakInFlight, _inFlight);
        }

        try
        {
            if (Answer is { } answer)
                return Task.FromResult(answer(request));
            if (Identification is { } identification)
                return Task.FromResult(IntegrationCallResult<LibraryIdentification>.Succeeded(identification(request)));
            return Task.FromResult(IntegrationCallResult<LibraryIdentification>.Failed(IntegrationCallStatus.Unreachable, Message.Legacy(UnarmedMessage)));
        }
        finally
        {
            lock (_flightGate)
                _inFlight--;
        }
    }

    /// <inheritdoc/>
    public Task<IntegrationCallResult<LibraryItemPage>> ListItemsAsync(
        IntegrationConnectionSettings settings, LibraryPageRequest page, CancellationToken cancellationToken = default)
    {
        _calls.Enqueue(settings);
        return Task.FromResult(ItemPages?.Invoke(page) ?? IntegrationCallResult<LibraryItemPage>.Failed(IntegrationCallStatus.Unreachable, Message.Legacy(UnarmedMessage)));
    }

    /// <inheritdoc/>
    public Task<IntegrationCallResult<IReadOnlyList<LibraryLocation>>> ListLocationsAsync(
        IntegrationConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        _calls.Enqueue(settings);
        return Task.FromResult(IntegrationCallResult<IReadOnlyList<LibraryLocation>>.Succeeded(Locations ?? []));
    }

    /// <inheritdoc/>
    public Task<IntegrationCallResult<LibraryPoster>> GetPosterAsync(
        IntegrationConnectionSettings settings, string sourceItemId, string posterTag, CancellationToken cancellationToken = default)
    {
        _calls.Enqueue(settings);
        return Task.FromResult(
            Posters?.Invoke(sourceItemId, posterTag)
            ?? IntegrationCallResult<LibraryPoster>.Failed(IntegrationCallStatus.Unreachable, Message.Legacy(UnarmedMessage)));
    }
}
