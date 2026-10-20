using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;

// MAM-34 (0.2.1) — the library contract's one change (IMediaLibraryItemIdentifier): Jellyfin's own "Identify", the owner's
// TMDb link of a film or a show applied to its movie or series item (Vasyl, 2026-10-06, ADR 0026). It replaces the
// earlier ids-only rule, which wrote ProviderIds with POST /Items/{id} and asked for "Search for missing metadata": the ids
// alone changed nothing anyone could see, and the title, overview and images are what the owner wants replaced.
//
// What it stands on was recorded live on Jellyfin 12.1.0 on 2026-10-06 (VendorSamples/Jellyfin: the remote-search*,
// *-remote-search-apply-response.txt and *-after-identify.json files): Identify is a search by id and one synchronous
// Apply. POST /Items/RemoteSearch/{Movie|Series} with {"SearchInfo":{"ProviderIds":{"Tmdb":"223021"}},"ItemId":…,
// "SearchProviderName":"TheMovieDb"} answers 200 with the provider's rows and changes nothing; POST /Items/RemoteSearch/
// Apply/{id}?replaceAllImages=true with the chosen row as its body answers 204 once the work is done — 1.8 s for a film,
// 21.3 s for an episode through the Open Movie Database, 159.3 s for a series of 149 episodes — and has replaced the
// item's name, year, ids, overview and images in the language of the item's library. Read in Jellyfin's source (v12.1):
// Apply runs with no cancellation, so it carries on after a client gives up, and with replaceAllImages it deletes the
// item's images before the providers run (MetadataService.cs:112-118), so an Apply that fails halfway leaves an item
// without them; and the search's ItemId only picks the providers from the item's library options — the language comes from
// the body, and falls back to the server's, whereas Apply's refresh uses the library's own (ProviderManager.cs:934-977).
//
// So an identification is three calls (HttpIntegrationAdapter.CallAsync), each with the shared fixed sentences for a
// transport failure: PrepareAsync (the reads, the checks, the search: nothing changes), ApplyAsync (one request, on a
// client of its own with a 20-minute timeout, never sent twice) and ConfirmAsync (the item read again). What is changed
// stays inside Apply: nothing here sends an item back, asks for a refresh or sets a lock.
internal sealed partial class JellyfinAdapter
{
    /// <summary>The search provider that answers by a TMDb id (recorded live: without a provider name only it answered a
    /// <c>Tmdb</c> search).</summary>
    private const string SearchProviderTmdb = "TheMovieDb";

    /// <summary>The search provider that finds an episode by its IMDb id: the only route recorded live for an episode a
    /// library lists as a film ("TheMovieDb" answered <c>[]</c> for the same id).</summary>
    private const string SearchProviderOmdb = "The Open Movie Database";

    // MAM-47 (ADR 0042) — each sentence a key of jellyfin.json (identify.*) with its values: the ids, the status and the
    // type Jellyfin holds are values; which registry an id belongs to ("TMDb id", "IMDb id") and which kind the link needs
    // ("not a movie") are part of the key, never words put into a sentence.
    private static readonly Message EpisodeUnidentifiableReason = Message.Of("jellyfin.identify.episode-unidentifiable");

    private static readonly Message NoAdministratorError = Message.Of("jellyfin.identify.no-administrator");

    private static readonly Message ItemGoneError = Message.Of("jellyfin.identify.item-gone");

    private static readonly Message HiddenFromAdministratorError = Message.Of("jellyfin.identify.hidden-from-administrator");

    private static readonly Message SearchForbiddenError = Message.Of("jellyfin.identify.search-forbidden");

    private static readonly Message SearchRefusedError = Message.Of("jellyfin.identify.search-refused");

    private static readonly Message ApplyForbiddenError = Message.Of("jellyfin.identify.apply-forbidden");

    private static readonly Message ApplyRefusedError = Message.Of("jellyfin.identify.apply-refused");

    // The word the owner reads is "locked", the contract's member is ItemProtected (the convention test forbids the word in
    // member names).
    private static readonly Message ItemProtectedError = Message.Of("jellyfin.identify.item-locked");

    // R10 — Apply left the app and what became of it is unknown: the identification is still a value, sent and not
    // confirmed, so the caller records it as one that may have landed. Never sent again (house rule 8): the owner's Retry reads
    // the item first and only sends again when it still doesn't show the match.
    private static readonly Message AnswerLostProblem = Message.Of("jellyfin.identify.answer-lost");

    private static readonly Message NotConfirmedProblem = Message.Of("jellyfin.identify.not-confirmed");

    /// <inheritdoc/>
    /// <remarks>Films and series; an episode item is never identified: Jellyfin has no search for an episode, and its Apply
    /// takes an item id without a type check, so an episode would be given its series' ids.</remarks>
    public IReadOnlyList<MediaWorkKind> IdentifiableKinds { get; } = [MediaWorkKind.Movie, MediaWorkKind.Series];

    /// <inheritdoc/>
    /// <remarks>MAM-47 — an episode's is the one there is: films and series are what it identifies
    /// (<see cref="IdentifiableKinds"/>), so asking why one of them stays in the app is the caller's mistake, never a
    /// sentence a translator writes for nothing.</remarks>
    public Message UnidentifiableKindReason(MediaWorkKind kind) =>
        kind == MediaWorkKind.Episode
            ? EpisodeUnidentifiableReason
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only an episode item's link stays in the app: Jellyfin identifies movie and series items.");

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Reads, as the first enabled administrator (Jellyfin reads one item only as a user): <c>GET /Users</c>,
    /// <c>GET /Items/{id}?userId=</c> and <c>GET /Library/VirtualFolders</c>, then searches —
    /// <c>POST /Items/RemoteSearch/{Movie|Series}</c> — and, unless a refusal or the item already showing the result ends it
    /// there, sends <c>POST /Items/RemoteSearch/Apply/{id}?replaceAllImages=true</c> once and reads the item again. An item
    /// the administrator can't see answers 404 like one that is gone; a user-less <c>GET /Items?ids=</c> tells the two
    /// apart. Every request carries the key in the <c>Authorization</c> header only.
    /// </para>
    /// <para>
    /// Apply is sent once, whatever becomes of it (house rule 8). A refusal (400, 401, 403, 404) changed nothing and fails
    /// the call. Anything after which the item may have changed — a 2xx, a 5xx, an answer lost or late — is a value, sent:
    /// <see cref="LibraryIdentifyOutcome.Identified"/> when the item read again shows the match, whatever the answer was, and
    /// otherwise <see cref="LibraryIdentifyOutcome.NotConfirmed"/> with the sentence saying why not. The item is read again
    /// whole after Apply because Jellyfin's answer says nothing about what it did.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<LibraryIdentification>> IdentifyAsync(
        IntegrationConnectionSettings settings, LibraryIdentifyRequest request, CancellationToken cancellationToken = default)
    {
        // A caller's mistake, thrown before anything is sent.
        var plan = ValidateRequest(request);
        return IdentifyCoreAsync(settings, plan, cancellationToken);
    }

    private async Task<IntegrationCallResult<LibraryIdentification>> IdentifyCoreAsync(
        IntegrationConnectionSettings settings, IdentifyPlan plan, CancellationToken cancellationToken)
    {
        var prepared = await CallAsync<Preparation>(
            settings, (client, sessionSecrets, token) => PrepareAsync(client, settings, plan, sessionSecrets, token), cancellationToken);
        if (!prepared.IsSuccess)
            return IntegrationCallResult<LibraryIdentification>.Failed(prepared.Status, prepared.Error!);
        if (prepared.Value!.Decided is { } decided)
            return IntegrationCallResult<LibraryIdentification>.Succeeded(Masked(decided, settings));

        var ready = prepared.Value.Ready!;
        // Its own client, so its own timeout: the shared 15 seconds would give up on a series long before Jellyfin is done.
        var applied = await CallAsync<ApplyAnswer>(
            settings,
            (client, sessionSecrets, token) => ApplyAsync(client, settings, plan, ready, sessionSecrets, token),
            cancellationToken,
            IdentifyHttpClientName);
        if (!applied.IsSuccess)
            return IntegrationCallResult<LibraryIdentification>.Failed(applied.Status, applied.Error!);

        // A separate call, so a transport failure or a timeout here gets the shared sentence and still leaves the
        // identification sent rather than failed.
        var readAgain = await CallAsync<ItemAfterApply>(
            settings, (client, sessionSecrets, token) => ReadAgainAsync(client, settings, plan, ready, sessionSecrets, token), cancellationToken);
        if (!readAgain.IsSuccess)
        {
            return IntegrationCallResult<LibraryIdentification>.Succeeded(Masked(
                new LibraryIdentification(
                    LibraryIdentifyOutcome.NotConfirmed,
                    null,
                    Sent: true,
                    ready.IdsBefore,
                    ready.IdsIntended,
                    // The failed read's own sentence after it, whatever it is (a shared transport one included).
                    Message.Of("jellyfin.identify.read-again-failed", ("error", readAgain.Error!))),
                settings));
        }

        var after = readAgain.Value!;
        var confirmed = Confirms(plan, ready, after.Facts);
        return IntegrationCallResult<LibraryIdentification>.Succeeded(Masked(
            confirmed
                ? new LibraryIdentification(LibraryIdentifyOutcome.Identified, null, Sent: true, ready.IdsBefore, after.Ids, null)
                : new LibraryIdentification(
                    LibraryIdentifyOutcome.NotConfirmed, null, Sent: true, ready.IdsBefore, after.Ids, applied.Value!.Problem ?? NotConfirmedProblem),
            settings));
    }

    // ── call 1: the reads, the checks and the search ─────────────────────────

    /// <summary>
    /// Everything up to the Apply, none of which changes anything: the administrator, the item, its library's options, the
    /// lock, the search, the one row that carries the id, the held ids and the shortcut. A decision that needs no Apply —
    /// a wrong type, a library that saves beside the media, a lock, no match, a conflict, an item already showing the
    /// result — is a value; a failed read or search fails the call.
    /// </summary>
    private async Task<IntegrationCallResult<Preparation>> PrepareAsync(
        HttpClient client, IntegrationConnectionSettings settings, IdentifyPlan plan, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var user = await ReadAdministratorAsync(client, settings, sessionSecrets, cancellationToken);
        if (!user.IsSuccess)
            return IntegrationCallResult<Preparation>.Failed(user.Status, user.Error!);
        var userId = user.Value!;

        var read = await ReadItemAsync(client, settings, plan.ItemId, userId, sessionSecrets, cancellationToken);
        if (!read.IsSuccess)
            return IntegrationCallResult<Preparation>.Failed(read.Status, read.Error!);

        ItemFacts before;
        List<MediaIdentifier> idsBefore;
        using (var document = read.Value!)
        {
            before = ItemFactsOf(document.RootElement);
            idsBefore = Identifiers(document.RootElement, plan.Kind);
        }

        // An item of another type is never identified: its ids would mean something else (a show's TMDb number is not a
        // film's, ADR 0017), and Jellyfin's Apply wouldn't check.
        if (!string.Equals(before.Type, TypeName(plan.Kind), StringComparison.Ordinal))
            return Decided(LibraryIdentifyOutcome.Refused, LibraryIdentifyRefusal.KindNotIdentifiable, WrongType(before.Type, plan.Kind, CallSecrets(settings, sessionSecrets)), [], []);

        // Before any search (decision 5 of ADR 0026): files and folders are never touched.
        var library = await ReadLibraryOptionsAsync(client, settings, before.Path, sessionSecrets, cancellationToken);
        if (!library.IsSuccess)
            return IntegrationCallResult<Preparation>.Failed(library.Status, library.Error!);
        if (library.Value!.Refusal is { } libraryRefusal)
            return Decided(LibraryIdentifyOutcome.Refused, libraryRefusal, library.Value.Sentence!, idsBefore, idsBefore);

        // The whole item, or its name or overview, locked: Apply deletes the images first, and a locked name would fail the
        // confirmation after that, while a locked overview would keep the old text and still pass it.
        if (before.LockData || before.NameLocked || before.OverviewLocked)
            return Decided(LibraryIdentifyOutcome.Refused, LibraryIdentifyRefusal.ItemProtected, ItemProtectedError, idsBefore, idsBefore);

        var search = await SearchAsync(client, settings, plan, library.Value, sessionSecrets, cancellationToken);
        if (!search.IsSuccess)
            return IntegrationCallResult<Preparation>.Failed(search.Status, search.Error!);

        var (row, noMatch) = ChooseRow(search.Value!, plan);
        if (row is null)
            return Decided(LibraryIdentifyOutcome.NoMatch, null, noMatch!, idsBefore, idsBefore);

        if (plan.ProtectHeldIds && ConflictOf(before.ProviderIds, row, plan) is { } conflict)
            return Decided(LibraryIdentifyOutcome.Refused, LibraryIdentifyRefusal.ConflictingId, conflict, idsBefore, idsBefore);

        if (AlreadyShows(before, row, plan))
        {
            return IntegrationCallResult<Preparation>.Succeeded(new Preparation(
                new LibraryIdentification(LibraryIdentifyOutcome.AlreadyIdentified, null, Sent: false, idsBefore, idsBefore, null), null));
        }

        return IntegrationCallResult<Preparation>.Succeeded(new Preparation(null, new ReadyToApply(userId, before, row, idsBefore, row.Ids)));
    }

    /// <summary>
    /// <c>POST /Items/RemoteSearch/{Movie|Series}</c>: the provider's rows for the id. The body is exactly what the live
    /// check sent — the key in Jellyfin's own spelling (<c>tmdb</c> answers <c>[]</c>), the item's id, which only picks the
    /// providers from its library's options, and the provider's name — plus the library's language and country when it has
    /// one: left out, Jellyfin searches in the server's language, while Apply's refresh uses the library's, so the row would
    /// carry a name the item never gets. Blank means none, and is left out.
    /// </summary>
    private async Task<IntegrationCallResult<List<SearchRow>>> SearchAsync(
        HttpClient client, IntegrationConnectionSettings settings, IdentifyPlan plan, LibraryVerdict library, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, plan.SearchPath);
        request.Method = HttpMethod.Post;
        request.Content = new ByteArrayContent(SearchBody(plan, library)) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
        using var response = await client.SendAsync(request, cancellationToken);
        switch ((int)response.StatusCode)
        {
            case 200:
                break;
            case 400:
                return IntegrationCallResult<List<SearchRow>>.Failed(IntegrationCallStatus.Refused, SearchRefusedError);
            case 401:
                return IntegrationCallResult<List<SearchRow>>.Failed(IntegrationCallStatus.AuthenticationFailed, KeyRefusedError);
            case 403:
                return IntegrationCallResult<List<SearchRow>>.Failed(IntegrationCallStatus.Refused, SearchForbiddenError);
            default:
                return DescribeUnexpectedStatus<List<SearchRow>>(response, settings, sessionSecrets);
        }

        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Array } rows)
            return NotJellyfin<List<SearchRow>>();

        // The rows outlive the document: each is kept as its own text.
        return IntegrationCallResult<List<SearchRow>>.Succeeded(
            [.. rows.EnumerateArray().Select(row => new SearchRow(row.GetRawText()))]);
    }

    /// <summary>The search's body, compact, its members in the order the live check sent them.</summary>
    private static byte[] SearchBody(IdentifyPlan plan, LibraryVerdict library)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("SearchInfo");
            writer.WriteStartObject("ProviderIds");
            writer.WriteString(plan.Key, plan.Value);
            writer.WriteEndObject();
            if (library.Language is { } language)
            {
                writer.WriteString("MetadataLanguage", language);
                if (library.Country is { } country)
                    writer.WriteString("MetadataCountryCode", country);
            }

            writer.WriteEndObject();
            writer.WriteString("ItemId", plan.ItemId.ToString("D", CultureInfo.InvariantCulture));
            writer.WriteString("SearchProviderName", plan.Provider);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// The one row that carries exactly the requested id under exactly the requested key (a lower-case key never matches, a
    /// row found by another id — the namespace trap recorded live: a TV id searched as a film answers an unrelated film —
    /// never counts), or the sentence saying there is none: no row, more than one, or one without a name.
    /// </summary>
    private static (ChosenRow? Row, Message? NoMatch) ChooseRow(List<SearchRow> rows, IdentifyPlan plan)
    {
        List<ChosenRow> carrying = [];
        foreach (var searchRow in rows)
        {
            using var document = JsonDocument.Parse(searchRow.Json);
            var row = document.RootElement;
            if (row.ValueKind != JsonValueKind.Object
                || !row.TryGetProperty(ProviderIdsProperty, out var providerIds)
                || providerIds.ValueKind != JsonValueKind.Object)
                continue;
            var holds = providerIds.EnumerateObject().Any(property =>
                string.Equals(property.Name, plan.Key, StringComparison.Ordinal)
                && property.Value.ValueKind == JsonValueKind.String
                && string.Equals(property.Value.GetString()!.Trim(), plan.Value, StringComparison.OrdinalIgnoreCase));
            if (!holds)
                continue;

            carrying.Add(new ChosenRow(
                searchRow.Json,
                StringValue(row, "Name"),
                Number(row, "ProductionYear"),
                StringValue(row, "ImageUrl"),
                StringValue(row, "Overview"),
                Identifiers(row, plan.Kind)));
        }

        return carrying.Count switch
        {
            0 => (null, plan.NoMatch),
            1 when string.IsNullOrWhiteSpace(carrying[0].Name) => (null, plan.NoName),
            1 => (carrying[0], null),
            _ => (null, plan.Ambiguous),
        };
    }

    // ── the held ids ─────────────────────────────────────────────────────────

    /// <summary>
    /// The sentence refusing the identification because it would discard an id the item holds, or <c>null</c> when it
    /// would not (<see cref="LibraryIdentifyRequest.ProtectHeldIds"/>). "Held" is the item's well-formed value of the key,
    /// the first in any letter case, trimmed; a malformed or blank one is not held. A held value equal to the wanted one
    /// never conflicts, and neither do the Tvdb id and the other keys.
    /// </summary>
    /// <remarks>
    /// For a film or a show (its TMDb id): a different held TMDb id conflicts, a held IMDb id that differs from the one the
    /// row carries conflicts, and a held IMDb id the row carries none to compare with conflicts too — it fails closed,
    /// because the film another work's IMDb id sits on is exactly the mistake the guard exists for. For an episode on a
    /// film item (its IMDb id): only a different held IMDb id conflicts — a film's TMDb id on such an item is the one that is
    /// wrong, and Apply drops it (recorded live) —, and a held IMDb id there is typically the show's.
    /// </remarks>
    private static Message? ConflictOf(List<(string Key, string? Value)> held, ChosenRow row, IdentifyPlan plan)
    {
        var heldImdb = HeldId(held, "Imdb");
        if (string.Equals(plan.Key, "Imdb", StringComparison.Ordinal))
        {
            return heldImdb is not null && !SameValue(heldImdb, plan.Value)
                ? Message.Of("jellyfin.identify.conflicting-id.imdb", ("held", heldImdb), ("wanted", plan.Value))
                : null;
        }

        var heldTmdb = HeldId(held, "Tmdb");
        if (heldTmdb is not null && !SameValue(heldTmdb, plan.Value))
            return Message.Of("jellyfin.identify.conflicting-id.tmdb", ("held", heldTmdb), ("wanted", plan.Value));

        if (heldImdb is null)
            return null;
        var wantedImdb = row.Ids.FirstOrDefault(id => id.Scheme == MediaIdentifierScheme.Imdb)?.Value;
        if (wantedImdb is null)
            return Message.Of("jellyfin.identify.conflicting-id.imdb-uncheckable", ("held", heldImdb));
        return SameValue(heldImdb, wantedImdb)
            ? null
            : Message.Of("jellyfin.identify.conflicting-id.imdb", ("held", heldImdb), ("wanted", wantedImdb));
    }

    // ── the shortcut and the confirmation ────────────────────────────────────

    /// <summary>
    /// Whether the item already shows the row, so that sending Apply would only delete its images for nothing — the
    /// idempotence shortcut. All of these hold: its name equals the row's (trimmed, in any letter case); the row has no year
    /// or the item's equals it; the item holds the requested key with an equal value; when the row has an image, the item has
    /// a primary one; when the row has an overview, the item's equals it. Any doubt means Apply, the safe direction.
    /// </summary>
    /// <remarks>
    /// The overview is compared because Jellyfin's old "Search for missing metadata" — which the owner pressed for every
    /// link — leaves exactly the name, year, ids and a poster on an item: without it the shortcut could call an item
    /// identified whose text came from another provider. The comparison ignores how whitespace is laid out, never the
    /// letters. A row without an overview (every Open Movie Database row of an episode) has nothing to compare.
    /// </remarks>
    private static bool AlreadyShows(ItemFacts item, ChosenRow row, IdentifyPlan plan) =>
        SameName(item.Name, row.Name)
        && (row.Year is null || item.Year == row.Year)
        && HoldsKeyValue(item.ProviderIds, plan.Key, plan.Value)
        && (string.IsNullOrWhiteSpace(row.ImageUrl) || !string.IsNullOrWhiteSpace(item.PrimaryImageTag))
        && (string.IsNullOrWhiteSpace(row.Overview) || string.Equals(Normalise(item.Overview), Normalise(row.Overview), StringComparison.Ordinal));

    /// <summary>
    /// Whether the item read after Apply shows the match. Its name equals the row's, strictly — the row was searched in the
    /// library's own language, so another name means another provider supplied it —, its year and its key agree, Apply
    /// changed something (it always changes the Etag: an unchanged item can't be identified), and, when the row has an image,
    /// the item has a primary one: with <c>replaceAllImages</c> Jellyfin deletes the images before the providers run, so an
    /// item whose name already equalled the row's would otherwise pass while having lost its poster.
    /// </summary>
    /// <remarks>
    /// The overview is deliberately not compared here, unlike the shortcut: a re-read that failed on the spelling of the text
    /// would send the owner's Retry into a second destructive Apply, and the name, id and poster already prove the metadata
    /// phase ran. The shortcut errs towards Apply, the confirmation towards not repeating one.
    /// </remarks>
    private static bool Confirms(IdentifyPlan plan, ReadyToApply ready, ItemFacts after) =>
        SameName(after.Name, ready.Row.Name)
        && (ready.Row.Year is null || after.Year == ready.Row.Year)
        && HoldsKeyValue(after.ProviderIds, plan.Key, plan.Value)
        && (!string.Equals(after.Etag, ready.Before.Etag, StringComparison.Ordinal)
            || !string.Equals(after.PrimaryImageTag, ready.Before.PrimaryImageTag, StringComparison.Ordinal))
        && (string.IsNullOrWhiteSpace(ready.Row.ImageUrl) || !string.IsNullOrWhiteSpace(after.PrimaryImageTag));

    private static bool SameName(string? item, string? row) =>
        item is not null && row is not null && string.Equals(item.Trim(), row.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The text with every run of whitespace as one space and trimmed; none is the empty text.</summary>
    private static string Normalise(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ── call 2: Apply ────────────────────────────────────────────────────────

    /// <summary>
    /// The one request that changes the item: <c>POST /Items/RemoteSearch/Apply/{id}?replaceAllImages=true</c> with the
    /// chosen row exactly as Jellyfin gave it (the live check posted the whole row, its overview and artists included). It
    /// carries no language: Jellyfin applies the item's library's. <c>replaceAllImages</c> is the literal <c>true</c>, as
    /// Jellyfin's own button sends it, and no parameter of the contract can change it. Sent once, never retried.
    /// </summary>
    /// <remarks>
    /// A refusal (400, 401, 403, 404) changed nothing and fails the call. Anything after which the item may have changed — a
    /// 2xx, a 5xx, an answer lost or late — is an answer to confirm by reading the item again: Jellyfin's Apply runs with no
    /// cancellation, so a client that gave up has not stopped it.
    /// </remarks>
    private async Task<IntegrationCallResult<ApplyAnswer>> ApplyAsync(
        HttpClient client, IntegrationConnectionSettings settings, IdentifyPlan plan, ReadyToApply ready, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, string.Create(CultureInfo.InvariantCulture, $"Items/RemoteSearch/Apply/{plan.ItemId:N}?replaceAllImages=true"));
        // The same key and Accept as every read; only the method and the body differ.
        request.Method = HttpMethod.Post;
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(ready.Row.Json)) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
        // Never on a pooled connection: .NET's own handler sends a request again, silently, when a reused connection turns out
        // to have been closed by the other end before any answer came — and an Apply whose first send reached Jellyfin would
        // be a second identification (house rule 8: no retry). Every Apply asks for its connection to close, so none is kept
        // for the next one, which opens its own — and the first request of a connection is never sent again.
        request.Headers.ConnectionClose = true;

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception) when (MayHaveArrived(exception))
        {
            return IntegrationCallResult<ApplyAnswer>.Succeeded(new ApplyAnswer(AnswerLostProblem));
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && exception.InnerException is not TimeoutException { InnerException: null })
        {
            // The client's timeout ran out after the connection was made: Jellyfin may be working still. The handler's
            // connect timeout, a TimeoutException with nothing inside (see HttpIntegrationAdapter), sent nothing, and is left
            // to the shared sentence.
            return IntegrationCallResult<ApplyAnswer>.Succeeded(
                new ApplyAnswer(Message.Of("jellyfin.identify.answer-timed-out", ("count", Minutes(client.Timeout)))));
        }

        using (response)
        {
            return (int)response.StatusCode switch
            {
                >= 200 and < 300 => IntegrationCallResult<ApplyAnswer>.Succeeded(new ApplyAnswer(null)),
                // The answers to an Apply that did nothing (the 401 and 404 recorded live).
                400 => IntegrationCallResult<ApplyAnswer>.Failed(IntegrationCallStatus.Refused, ApplyRefusedError),
                401 => IntegrationCallResult<ApplyAnswer>.Failed(IntegrationCallStatus.AuthenticationFailed, KeyRefusedError),
                403 => IntegrationCallResult<ApplyAnswer>.Failed(IntegrationCallStatus.Refused, ApplyForbiddenError),
                404 => IntegrationCallResult<ApplyAnswer>.Failed(IntegrationCallStatus.NotFound, ItemGoneError),
                // The server failed while handling it, perhaps after deleting the images and before fetching new ones.
                >= 500 and < 600 => IntegrationCallResult<ApplyAnswer>.Succeeded(
                    new ApplyAnswer(Message.Of("jellyfin.identify.answer-failed", ("status", StatusText(response))))),
                _ => DescribeUnexpectedStatus<ApplyAnswer>(response, settings, sessionSecrets),
            };
        }
    }

    // ── call 3: the item read again ──────────────────────────────────────────

    /// <summary>The item read again as the same administrator: its facts and its registry ids as the sync reads them.</summary>
    private async Task<IntegrationCallResult<ItemAfterApply>> ReadAgainAsync(
        HttpClient client, IntegrationConnectionSettings settings, IdentifyPlan plan, ReadyToApply ready, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var read = await ReadItemAsync(client, settings, plan.ItemId, ready.UserId, sessionSecrets, cancellationToken);
        if (!read.IsSuccess)
            return IntegrationCallResult<ItemAfterApply>.Failed(read.Status, read.Error!);

        using var document = read.Value!;
        var facts = ItemFactsOf(document.RootElement);
        // An item that is another type now can't be the one that was identified: a read failure, not a match.
        if (!string.Equals(facts.Type, TypeName(plan.Kind), StringComparison.Ordinal))
            return IntegrationCallResult<ItemAfterApply>.Failed(IntegrationCallStatus.UnexpectedResponse, WrongType(facts.Type, plan.Kind, CallSecrets(settings, sessionSecrets)));
        return IntegrationCallResult<ItemAfterApply>.Succeeded(new ItemAfterApply(facts, Identifiers(document.RootElement, plan.Kind)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A caller's mistake, thrown before anything is sent: a missing request, an item id Jellyfin never gives (it goes into
    /// the path, so only a Guid is sent, MAM-8's rule), a kind <see cref="IdentifiableKinds"/> leaves out, a pair of kind and
    /// scheme the contract doesn't allow — a show's TMDb id never goes to the film search, nor a film's to the series search
    /// (ADR 0017) —, or a malformed id.
    /// </summary>
    private IdentifyPlan ValidateRequest(LibraryIdentifyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(request.SourceItemId, out var itemId))
            throw new ArgumentException("The item id is not one Jellyfin gives.", nameof(request));
        if (!IdentifiableKinds.Contains(request.Kind))
            throw new ArgumentException("Jellyfin identifies movie and series items only.", nameof(request));
        if (request.Identifier is null)
            throw new ArgumentException("The request names no id.", nameof(request));

        var (scheme, value) = (request.Identifier.Scheme, request.Identifier.Value);
        // MAM-47 — the sentences of no match name the registry by their key: "for TMDb id 223021", "for IMDb id tt9103946".
        var (key, provider, noMatch, ambiguous, noName) = (request.Kind, scheme) switch
        {
            (MediaWorkKind.Movie, MediaIdentifierScheme.Tmdb) => (
                "Tmdb", SearchProviderTmdb, "jellyfin.identify.no-match.tmdb-film", "jellyfin.identify.no-match.ambiguous-tmdb", "jellyfin.identify.no-match.no-name-tmdb"),
            (MediaWorkKind.Series, MediaIdentifierScheme.TmdbTv) => (
                "Tmdb", SearchProviderTmdb, "jellyfin.identify.no-match.tmdb-show", "jellyfin.identify.no-match.ambiguous-tmdb", "jellyfin.identify.no-match.no-name-tmdb"),
            (MediaWorkKind.Movie, MediaIdentifierScheme.Imdb) => (
                "Imdb", SearchProviderOmdb, "jellyfin.identify.no-match.omdb", "jellyfin.identify.no-match.ambiguous-imdb", "jellyfin.identify.no-match.no-name-imdb"),
            _ => throw new ArgumentException($"A {request.Kind} item is not identified by a {scheme} id.", nameof(request)),
        };
        if (value is null || !(scheme == MediaIdentifierScheme.Imdb ? MediaIdentifierRules.IsImdbId(value) : MediaIdentifierRules.IsTmdbId(value)))
            throw new ArgumentException($"The {scheme} value is not an id of it.", nameof(request));

        return new IdentifyPlan(
            itemId,
            request.Kind,
            key,
            value,
            provider,
            Message.Of(noMatch, ("id", value)),
            Message.Of(ambiguous, ("id", value)),
            Message.Of(noName, ("id", value)),
            request.Kind == MediaWorkKind.Series ? "Items/RemoteSearch/Series" : "Items/RemoteSearch/Movie",
            request.ProtectHeldIds);
    }

    private static IntegrationCallResult<Preparation> Decided(
        LibraryIdentifyOutcome outcome,
        LibraryIdentifyRefusal? refusal,
        Message sentence,
        IReadOnlyList<MediaIdentifier> idsBefore,
        IReadOnlyList<MediaIdentifier> idsAfter) =>
        IntegrationCallResult<Preparation>.Succeeded(new Preparation(new LibraryIdentification(outcome, refusal, Sent: false, idsBefore, idsAfter, sentence), null));

    /// <summary>Jellyfin holds an item of the type <paramref name="type"/> where the link needs one of <paramref name="kind"/>,
    /// a film or a series (<see cref="ValidateRequest"/> lets no other kind through).</summary>
    private static Message WrongType(string type, MediaWorkKind kind, string?[] secrets)
    {
        var held = VendorText.Clean(type, RemoteValueMaximumLength, secrets);
        return kind == MediaWorkKind.Series
            ? Message.Of("jellyfin.identify.wrong-type.series", ("type", held))
            : Message.Of("jellyfin.identify.wrong-type.movie", ("type", held));
    }

    /// <summary>
    /// The Apply client's timeout in minutes as the format <c>0.####</c> wrote them: a production timeout is whole minutes
    /// ("20"), a test's 100 milliseconds "0.0017" rather than a silly "0". MAM-47 — rounded here, half away from zero as that
    /// format rounded, before the template counts them (pinned by <c>EnglishFormatPinTests</c>).
    /// </summary>
    private static NumberValue Minutes(TimeSpan timeout) =>
        new(Math.Round((decimal)timeout.TotalMinutes, 4, MidpointRounding.AwayFromZero));

    /// <summary><paramref name="value"/> with its sentence masked of the call's secrets: unlike an error, a value is not
    /// masked on its way out (<see cref="HttpIntegrationAdapter.CallAsync{T}"/>).</summary>
    private static LibraryIdentification Masked(LibraryIdentification value, IntegrationConnectionSettings settings) =>
        value.Sentence is null ? value : value with { Sentence = value.Sentence.Redacted(settings.Secrets) };

    /// <summary>The first call's result: the identification when the call decided it, or what Apply needs.</summary>
    private sealed record Preparation(LibraryIdentification? Decided, ReadyToApply? Ready);

    /// <summary>What Apply needs and what the confirmation compares with: the administrator the item is read again as, the
    /// item as read before, the chosen row and the item's ids before and the ones the row means to leave.</summary>
    private sealed record ReadyToApply(
        string UserId, ItemFacts Before, ChosenRow Row, IReadOnlyList<MediaIdentifier> IdsBefore, IReadOnlyList<MediaIdentifier> IdsIntended);

    /// <summary>A search result as the identification reads it: its text exactly as Jellyfin sent it — the body of Apply —
    /// and the facts it is compared on.</summary>
    private sealed record ChosenRow(string Json, string? Name, int? Year, string? ImageUrl, string? Overview, IReadOnlyList<MediaIdentifier> Ids);

    /// <summary>A search result kept as text, so it outlives the document the answer was parsed from.</summary>
    private sealed record SearchRow(string Json);

    /// <summary>What Apply's call came to when it left the app: the sentence saying why the answer says nothing (lost, late,
    /// a server error), or <c>null</c> when Jellyfin answered 2xx.</summary>
    private sealed record ApplyAnswer(Message? Problem);

    /// <summary>The item read again after Apply.</summary>
    private sealed record ItemAfterApply(ItemFacts Facts, IReadOnlyList<MediaIdentifier> Ids);

    /// <summary>The request once validated: what to search for, where, and under which key and provider; and the sentences
    /// of no match — no row carries the id, more than one does, or the one that does has no name.</summary>
    private sealed record IdentifyPlan(
        Guid ItemId,
        MediaWorkKind Kind,
        string Key,
        string Value,
        string Provider,
        Message NoMatch,
        Message Ambiguous,
        Message NoName,
        string SearchPath,
        bool ProtectHeldIds);
}
