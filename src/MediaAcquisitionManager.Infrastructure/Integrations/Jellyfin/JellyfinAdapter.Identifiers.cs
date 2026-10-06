using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;

// MAM-34 — the library contract's one write (IMediaLibraryIdentifierWriter): the owner's TMDb link of a film set into
// its movie item's ProviderIds and nothing else (Vasyl, 2026-10-05: ids only — no identify, no refresh, no images,
// titles or locks). What it stands on was recorded live on Jellyfin 12.1.0 (VendorSamples/Jellyfin: users.json,
// item*.json and the .txt answers): Jellyfin reads one item only as a user — GET /Items/{id} with the API key and no
// user is 400 — so the item is read as the first enabled administrator GET /Users lists; POST /Items/{id} replaces the
// item with the body it is sent, so the body is the item as just read with only ProviderIds changed, and it answers
// 204; read again, the item shows the new ids, and besides them only what Jellyfin derives from them (Etag,
// ExternalUrls, UserData.Key) has changed. 10.9 to 11 are taken on trust from the same endpoints in their source, as the
// reads are (MAM-8); the caller's connection test is the version gate.
//
// What the same handler does beyond that, read in Jellyfin's source at tag v12.1 and the same on master
// (Jellyfin.Api/Controllers/ItemUpdateController.cs, UpdateItem), not seen live:
// - A series is never written (Vasyl, 2026-10-06): every POST of a series rewrites and saves each of its seasons and
//   episodes — their SeriesName and CustomRating always, their OfficialRating unless the child locked it — and each
//   save moves the child's DateLastSaved (and would rewrite its NFO file, were there one). That is far more than ids,
//   recorded nowhere and beyond any removal's reach, so WritableKinds holds films only and a series link stays in the
//   app. The live check of 2026-10-05 had written and removed the Penguins series' TMDb id twice (series-item*.json)
//   before this was read.
// - People is left out of the body: sent, it makes Jellyfin rebuild the item's people from their names, roles and types
//   alone (UpdateItem → LibraryManager.UpdatePeople), dropping what else it keeps of them; left out, the cast stays as it
//   is. Every other field goes back as read.
// - ProviderIds replace the item's ids as a whole (ProviderIdsExtensions.SetProviderIds), so Jellyfin itself drops any
//   id it held that fails its own format check: sent back as read, such an id is gone after the write — Jellyfin's
//   doing, not the app's.
internal sealed partial class JellyfinAdapter
{
    /// <summary>The name Jellyfin gives an item's registry ids, in every item it sends and takes.</summary>
    private const string ProviderIdsProperty = "ProviderIds";

    /// <summary>The name of an item's cast and crew, left out of what is sent back (see the top of this file).</summary>
    private const string PeopleProperty = "People";

    private const string SeriesUnwritableReason =
        "Jellyfin rewrites every season and episode of a series whenever it saves the series, so a series link stays in the app.";

    // {0} is the kind as a sentence names it ("an episode").
    private const string KindUnwritableReason = "Jellyfin's ids are written on movie items only, so a link on {0} stays in the app.";

    private const string NoAdministratorError = "Jellyfin has no enabled administrator account to read the item as.";

    private const string ItemGoneError = "Jellyfin no longer has this item; sync the library and link it again.";

    private const string WriteForbiddenError =
        "Jellyfin refused to change the item's ids. Check that the API key belongs to an administrator.";

    private const string WriteRefusedError = "Jellyfin refused the item it was sent back; nothing was changed.";

    // R10 — the change left the app, and what became of it is unknown: the write is still a success, sent and not
    // confirmed, so the caller records it as one that may have landed. Never sent again (house rule 8): the owner's
    // Retry reads the item first, and the next sync reads what Jellyfin holds.
    private const string AnswerLostProblem =
        "The change was sent to Jellyfin, but its answer never came, so the app can't tell whether Jellyfin saved it; the next sync shows what it holds.";

    // {0} is the client's timeout in seconds.
    private const string AnswerTimedOutProblem =
        "The change was sent to Jellyfin, but it didn't answer within {0} seconds, so the app can't tell whether it saved it; the next sync shows what it holds.";

    // {0} is the status, such as "500 Internal Server Error".
    private const string AnswerFailedProblem =
        "Jellyfin answered the change with {0}, so the app can't tell whether it saved it. Its own log says why; the next sync shows what it holds.";

    // {0} is the failed read's own sentence.
    private const string ReadAgainFailedProblem = "Jellyfin took the change, but the app couldn't read the item again to confirm it. {0}";

    private const string NotConfirmedProblem = "Jellyfin took the change, but the item read again holds other ids than the ones sent.";

    private const string HiddenFromAdministratorError =
        "Jellyfin's administrator account can't see this item (a parental limit, a blocked tag or no access to its library), so the app can't read it as that account.";

    /// <summary>
    /// The item's text goes back as Jellyfin sent it, UTF-8 rather than <c>\u</c> escapes. What the default encoder
    /// escapes on top guards HTML pages, which this body never reaches; it is read by Jellyfin's JSON reader alone.
    /// </summary>
    private static readonly JsonWriterOptions ItemWriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <inheritdoc/>
    /// <remarks>Films only: saving a series rewrites all its seasons and episodes (see the top of this file).</remarks>
    public IReadOnlyList<MediaWorkKind> WritableKinds { get; } = [MediaWorkKind.Movie];

    /// <inheritdoc/>
    public string UnwritableKindReason(MediaWorkKind kind) =>
        kind == MediaWorkKind.Series ? SeriesUnwritableReason : Sentence(KindUnwritableReason, kind);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A movie item only (<see cref="WritableKinds"/>): <c>GET /Users</c>, then <c>GET /Items/{id}?userId=</c> as the
    /// first enabled administrator, then — only when an edit changes something — one <c>POST /Items/{id}</c> with the
    /// item as read but for its <c>ProviderIds</c>, and without its <c>People</c>, then the same read again to confirm.
    /// An item the administrator can't see answers 404 like one that is gone; a user-less <c>GET /Items?ids=</c> tells
    /// the two apart. Every request carries the key the reads do, in the <c>Authorization</c> header only. Each id goes
    /// under Jellyfin's own key — <c>Tmdb</c>, <c>Imdb</c> — in the spelling the item already uses; a removed id loses
    /// its key; every other id (<c>TmdbCollection</c>, <c>Tvdb</c>, …) stays as read.
    /// </para>
    /// <para>
    /// The POST is sent once, whatever becomes of it (house rule 8). A refusal (400, 401, 403, 404) changed nothing and
    /// fails the call, as does an item hidden from the administrator (<see cref="IntegrationCallStatus.Refused"/>,
    /// nothing sent). Anything after which the item may have changed — a 2xx, a 5xx, an answer lost or late — is a
    /// success, sent: confirmed only when the read again shows every applied edit, and otherwise with the sentence
    /// saying why not (R10). A 3xx or another 4xx means the change went nowhere and fails with the shared sentence.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<LibraryIdentifierWrite>> WriteIdentifiersAsync(
        IntegrationConnectionSettings settings, LibraryIdentifierChange change, CancellationToken cancellationToken = default)
    {
        var itemId = ValidateChange(change);
        return WriteAsync(settings, itemId, change, cancellationToken);
    }

    /// <summary>
    /// The two calls of a write: the read, the change and the one POST in the first; the read again in the second,
    /// so a transport failure or a timeout there gets the shared sentence (<see cref="HttpIntegrationAdapter.CallAsync{T}"/>)
    /// and still leaves the write sent rather than failed.
    /// </summary>
    private async Task<IntegrationCallResult<LibraryIdentifierWrite>> WriteAsync(
        IntegrationConnectionSettings settings, Guid itemId, LibraryIdentifierChange change, CancellationToken cancellationToken)
    {
        var sent = await CallAsync<SentChange>(
            settings, (client, sessionSecrets, token) => ReadAndSendAsync(client, settings, itemId, change, sessionSecrets, token), cancellationToken);
        if (!sent.IsSuccess)
            return IntegrationCallResult<LibraryIdentifierWrite>.Failed(sent.Status, sent.Error!);

        var (write, userId) = sent.Value!;
        // Nothing posted, or posted with its answer lost: there is nothing a read now could confirm.
        if (!write.Sent || write.Problem is not null)
            return IntegrationCallResult<LibraryIdentifierWrite>.Succeeded(Masked(write, settings));

        var readAgain = await CallAsync<LibraryIdentifierWrite>(
            settings, (client, sessionSecrets, token) => ReadAgainAsync(client, settings, itemId, userId, change, write, sessionSecrets, token), cancellationToken);
        var confirmed = readAgain.IsSuccess
            ? readAgain.Value!
            : write with { Problem = string.Format(CultureInfo.InvariantCulture, ReadAgainFailedProblem, readAgain.Error) };
        return IntegrationCallResult<LibraryIdentifierWrite>.Succeeded(Masked(confirmed, settings));
    }

    /// <summary>
    /// The administrator, the item as read, the edits applied to its <c>ProviderIds</c> and — only when one changed
    /// something — the one POST. A refusal or a failure before the POST fails the call; once it has left, the call
    /// succeeds whatever its answer (<see cref="WriteIdentifiersAsync"/>).
    /// </summary>
    private async Task<IntegrationCallResult<SentChange>> ReadAndSendAsync(
        HttpClient client, IntegrationConnectionSettings settings, Guid itemId, LibraryIdentifierChange change, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var user = await ReadAdministratorAsync(client, settings, sessionSecrets, cancellationToken);
        if (!user.IsSuccess)
            return IntegrationCallResult<SentChange>.Failed(user.Status, user.Error!);
        var userId = user.Value!;

        var read = await ReadItemAsync(client, settings, itemId, userId, change.Kind, sessionSecrets, cancellationToken);
        if (!read.IsSuccess)
            return IntegrationCallResult<SentChange>.Failed(read.Status, read.Error!);

        byte[] body;
        LibraryIdentifierWrite write;
        using (var document = read.Value!)
        {
            var item = document.RootElement;
            var providerIds = ProviderIdPairs(item);
            var before = WellFormedIds(providerIds, change);
            List<LibraryIdentifierEdit> applied = [.. change.Edits.Where(edit => Changes(edit, CurrentValue(providerIds, ProviderIdKey(edit.Scheme))))];
            // Nothing to change: nothing is posted, and the item as read is what it holds now.
            if (applied.Count == 0)
                return IntegrationCallResult<SentChange>.Succeeded(new SentChange(new LibraryIdentifierWrite(before, before, [], Sent: false, Confirmed: true), userId));

            body = ItemWithProviderIds(item, applied);
            write = new LibraryIdentifierWrite(before, [], [.. applied.Select(edit => edit.Scheme)], Sent: true, Confirmed: false);
        }

        using var request = CreateRequest(settings, ItemPath(itemId));
        // The same key and Accept as every read; only the method and the body differ.
        request.Method = HttpMethod.Post;
        request.Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception) when (MayHaveArrived(exception))
        {
            return IntegrationCallResult<SentChange>.Succeeded(new SentChange(write with { Problem = AnswerLostProblem }, userId));
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && exception.InnerException is not TimeoutException { InnerException: null })
        {
            // The client's timeout ran out after the connection was made: the change may be in Jellyfin already (a
            // series write the live check made took 4.5 seconds). The handler's connect timeout, a TimeoutException with
            // nothing inside (see HttpIntegrationAdapter), sent nothing, and is left to the shared sentence.
            var problem = string.Format(CultureInfo.InvariantCulture, AnswerTimedOutProblem, client.Timeout.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture));
            return IntegrationCallResult<SentChange>.Succeeded(new SentChange(write with { Problem = problem }, userId));
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            return status switch
            {
                >= 200 and < 300 => IntegrationCallResult<SentChange>.Succeeded(new SentChange(write, userId)),
                // Jellyfin's answers to a change it didn't make (the 401 and 404 recorded live).
                400 => IntegrationCallResult<SentChange>.Failed(IntegrationCallStatus.Refused, WriteRefusedError),
                401 => IntegrationCallResult<SentChange>.Failed(IntegrationCallStatus.AuthenticationFailed, KeyRefusedError),
                403 => IntegrationCallResult<SentChange>.Failed(IntegrationCallStatus.Refused, WriteForbiddenError),
                404 => IntegrationCallResult<SentChange>.Failed(IntegrationCallStatus.NotFound, ItemGoneError),
                // The server failed while handling the change, perhaps after saving part of it.
                >= 500 and < 600 => IntegrationCallResult<SentChange>.Succeeded(new SentChange(
                    write with { Problem = string.Format(CultureInfo.InvariantCulture, AnswerFailedProblem, StatusText(response)) }, userId)),
                _ => DescribeUnexpectedStatus<SentChange>(response, settings, sessionSecrets),
            };
        }
    }

    /// <summary>
    /// The item read again as the same administrator: the ids of the edited schemes it holds now, and whether they
    /// are what <paramref name="write"/> sent.
    /// </summary>
    private async Task<IntegrationCallResult<LibraryIdentifierWrite>> ReadAgainAsync(
        HttpClient client,
        IntegrationConnectionSettings settings,
        Guid itemId,
        string userId,
        LibraryIdentifierChange change,
        LibraryIdentifierWrite write,
        ICollection<string> sessionSecrets,
        CancellationToken cancellationToken)
    {
        var read = await ReadItemAsync(client, settings, itemId, userId, change.Kind, sessionSecrets, cancellationToken);
        if (!read.IsSuccess)
            return IntegrationCallResult<LibraryIdentifierWrite>.Failed(read.Status, read.Error!);

        using var document = read.Value!;
        var providerIds = ProviderIdPairs(document.RootElement);
        var confirmed = change.Edits
            .Where(edit => write.Applied.Contains(edit.Scheme))
            .All(edit => SameValue(CurrentValue(providerIds, ProviderIdKey(edit.Scheme)), edit.Value));
        return IntegrationCallResult<LibraryIdentifierWrite>.Succeeded(write with
        {
            After = WellFormedIds(providerIds, change),
            Confirmed = confirmed,
            Problem = confirmed ? null : NotConfirmedProblem,
        });
    }

    // ── the reads ────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>GET /Users</c>: the id of the first user, in Jellyfin's order, who is an administrator and not disabled —
    /// Jellyfin reads one item only as a user, and an administrator sees every library. Each user is read for its id
    /// and its two policy flags only.
    /// </summary>
    private async Task<IntegrationCallResult<string>> ReadAdministratorAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, "Users");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return DescribeLibraryCallStatus<string>(response, NotFoundError("/Users"), settings, sessionSecrets);

        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Array } users)
            return NotJellyfin<string>();

        foreach (var user in users.EnumerateArray())
        {
            if (user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("Policy", out var policy)
                && policy.ValueKind == JsonValueKind.Object
                && policy.TryGetProperty("IsAdministrator", out var isAdministrator)
                && isAdministrator.ValueKind == JsonValueKind.True
                && !(policy.TryGetProperty("IsDisabled", out var isDisabled) && isDisabled.ValueKind == JsonValueKind.True)
                // The id goes into the item's query, so only one that is an id Jellyfin gives (MAM-8's rule for paths).
                && IdProperty(user, "Id") is { } id
                && Guid.TryParse(id, out var userId))
                return IntegrationCallResult<string>.Succeeded(userId.ToString("N"));
        }

        return IntegrationCallResult<string>.Failed(IntegrationCallStatus.Refused, NoAdministratorError);
    }

    /// <summary>
    /// <c>GET /Items/{id}?userId=</c>: the whole item, as a document the caller disposes, once it is the kind the link
    /// needs — a movie item for a film — with its <c>ProviderIds</c> an object or absent. An item of another type is
    /// never written: its ids would mean something else.
    /// </summary>
    private async Task<IntegrationCallResult<JsonDocument>> ReadItemAsync(
        HttpClient client, IntegrationConnectionSettings settings, Guid itemId, string userId, MediaWorkKind kind, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, string.Create(CultureInfo.InvariantCulture, $"{ItemPath(itemId)}?userId={userId}"));
        using var response = await client.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                break;
            case HttpStatusCode.NotFound:
                return await ItemMissingAsync(client, settings, itemId, sessionSecrets, cancellationToken);
            // 401 and 403 as any library read; a 400 — the live answer to a read without a user — is the shared
            // "doesn't send this" sentence: the app always names one.
            default:
                return DescribeLibraryCallStatus<JsonDocument>(response, ItemGoneError, settings, sessionSecrets);
        }

        var document = await TryReadJsonAsync(response, cancellationToken);
        var error = document?.RootElement is { ValueKind: JsonValueKind.Object } item
            ? ItemError(item, kind, CallSecrets(settings, sessionSecrets))
            : NotJellyfinError;
        if (error is null)
            return IntegrationCallResult<JsonDocument>.Succeeded(document!);

        document?.Dispose();
        return IntegrationCallResult<JsonDocument>.Failed(IntegrationCallStatus.UnexpectedResponse, error);
    }

    /// <summary>
    /// Why the item read as the administrator answered 404. Jellyfin answers 404 for an item that user may not see as for
    /// one that is gone (<c>LibraryManager.GetItemById</c> with a user: a parental limit, a blocked tag, a library the user
    /// has no access to — administrators included), while the sync's user-less list still holds it. So the same user-less
    /// list is asked for that one id (<c>GET /Items?ids=</c>, read in Jellyfin's source, not tried live): listed, the item
    /// is there but hidden, and the call is refused with its own sentence — a removal then keeps its link instead of
    /// deleting it with the app's ids left in Jellyfin; not listed, it is gone.
    /// </summary>
    private async Task<IntegrationCallResult<JsonDocument>> ItemMissingAsync(
        HttpClient client, IntegrationConnectionSettings settings, Guid itemId, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            settings,
            string.Create(CultureInfo.InvariantCulture, $"Items?ids={itemId:N}&recursive=true&enableImages=false&enableUserData=false&enableTotalRecordCount=true"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return DescribeLibraryCallStatus<JsonDocument>(response, ItemGoneError, settings, sessionSecrets);

        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("Items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return NotJellyfin<JsonDocument>();

        var listed = items.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.Object && IdProperty(item, "Id") is { } id && Guid.TryParse(id, out var listedId) && listedId == itemId);
        return listed
            ? IntegrationCallResult<JsonDocument>.Failed(IntegrationCallStatus.Refused, HiddenFromAdministratorError)
            : IntegrationCallResult<JsonDocument>.Failed(IntegrationCallStatus.NotFound, ItemGoneError);
    }

    /// <summary>Why an item read whole can't be written as <paramref name="kind"/>; <c>null</c> when it can.</summary>
    private static string? ItemError(JsonElement item, MediaWorkKind kind, string?[] secrets)
    {
        if (!item.TryGetProperty("Type", out var type) || type.ValueKind != JsonValueKind.String)
            return NotJellyfinError;
        if (!string.Equals(type.GetString(), TypeName(kind), StringComparison.Ordinal))
            return $"Jellyfin holds an item of type “{RemoteText.Clean(type.GetString(), RemoteValueMaximumLength, secrets)}” under this id, not {Sentence("{0}", kind)}, so the app left its ids alone. Sync the library and link it again.";
        return item.TryGetProperty(ProviderIdsProperty, out var providerIds) && providerIds.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)
            ? NotJellyfinError
            : null;
    }

    // ── the change ───────────────────────────────────────────────────────────

    /// <summary>
    /// The item as read, property by property in its order, every value written back as it came, with each
    /// <c>ProviderIds</c> replaced by the edited one (<see cref="WriteProviderIds"/>) — or one added at the end when the
    /// item had none — and no <c>People</c>: sent, Jellyfin would rebuild the cast from less than it holds of it (see
    /// the top of this file).
    /// </summary>
    private static byte[] ItemWithProviderIds(JsonElement item, IReadOnlyList<LibraryIdentifierEdit> applied)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, ItemWriterOptions))
        {
            writer.WriteStartObject();
            var replaced = false;
            foreach (var property in item.EnumerateObject())
            {
                if (string.Equals(property.Name, PeopleProperty, StringComparison.Ordinal))
                    continue;
                if (string.Equals(property.Name, ProviderIdsProperty, StringComparison.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteProviderIds(writer, item, applied);
                    replaced = true;
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            if (!replaced)
            {
                writer.WritePropertyName(ProviderIdsProperty);
                WriteProviderIds(writer, item, applied);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// The item's provider ids with <paramref name="applied"/>'s edits: an edited key keeps its place and its first
    /// spelling (any letter case), a further spelling of it is dropped — Jellyfin's ids ignore case, so it would
    /// overwrite the edit —, a removed one is gone, a new one comes last under Jellyfin's own name; every other id is
    /// written back as it came.
    /// </summary>
    private static void WriteProviderIds(Utf8JsonWriter writer, JsonElement item, IReadOnlyList<LibraryIdentifierEdit> applied)
    {
        writer.WriteStartObject();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetProperty(ProviderIdsProperty, out var element) && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var edit = applied.FirstOrDefault(edit => string.Equals(ProviderIdKey(edit.Scheme), property.Name, StringComparison.OrdinalIgnoreCase));
                if (edit is null)
                    property.WriteTo(writer);
                else if (done.Add(ProviderIdKey(edit.Scheme)) && edit.Value is not null)
                    writer.WriteString(property.Name, edit.Value);
            }
        }

        foreach (var edit in applied.Where(edit => edit.Value is not null && !done.Contains(ProviderIdKey(edit.Scheme))))
            writer.WriteString(ProviderIdKey(edit.Scheme), edit.Value);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Whether <paramref name="edit"/> changes the item, given the value <paramref name="current"/> it holds now: its
    /// rule holds and the value differs. A blank value is no value; a malformed one is still held, so the app never
    /// writes over anything Jellyfin has where it means not to.
    /// </summary>
    private static bool Changes(LibraryIdentifierEdit edit, string? current)
    {
        var holds = edit.Rule switch
        {
            LibraryIdentifierEditRule.OnlyWhereAbsent => current is null,
            LibraryIdentifierEditRule.OnlyWhereExpected => SameValue(current, edit.Expected),
            _ => true,
        };
        return holds && !SameValue(current, edit.Value);
    }

    // ── provider ids ─────────────────────────────────────────────────────────

    /// <summary>The item's provider ids in Jellyfin's order: each key with its text, <c>null</c> for a value that isn't
    /// a string. None when the item has no <c>ProviderIds</c> object.</summary>
    private static List<(string Key, string? Value)> ProviderIdPairs(JsonElement item) =>
        item.TryGetProperty(ProviderIdsProperty, out var providerIds) && providerIds.ValueKind == JsonValueKind.Object
            ?
            [
                .. providerIds.EnumerateObject().Select(property =>
                    (property.Name, property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null)),
            ]
            : [];

    /// <summary>The first non-blank value under <paramref name="key"/> in any letter case, trimmed; <c>null</c> when
    /// there is none. What a rule compares with — malformed or not.</summary>
    private static string? CurrentValue(List<(string Key, string? Value)> providerIds, string key) =>
        providerIds
            .Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value!.Trim())
            .FirstOrDefault();

    /// <summary>
    /// The item's ids of the edited schemes, in the edits' order: the first well-formed value of each, as a sync
    /// reads it (<see cref="Identifiers"/>).
    /// </summary>
    private static List<MediaIdentifier> WellFormedIds(List<(string Key, string? Value)> providerIds, LibraryIdentifierChange change) =>
    [
        .. change.Edits
            .Select(edit => providerIds
                .Where(pair => string.Equals(pair.Key, ProviderIdKey(edit.Scheme), StringComparison.OrdinalIgnoreCase) && pair.Value is not null)
                .Select(pair => pair.Value!.Trim())
                .Where(value => IsWellFormed(edit.Scheme, value))
                .Select(value => new MediaIdentifier(edit.Scheme, value))
                .FirstOrDefault())
            .OfType<MediaIdentifier>(),
    ];

    /// <summary>Jellyfin's key for a scheme a movie item takes: <c>Imdb</c>, and <c>Tmdb</c> for the film's TMDb
    /// id.</summary>
    private static string ProviderIdKey(MediaIdentifierScheme scheme) => scheme == MediaIdentifierScheme.Imdb ? "Imdb" : "Tmdb";

    /// <summary>Two values as the same id: both none, or equal once trimmed, in any letter case.</summary>
    private static bool SameValue(string? current, string? wanted) =>
        current is null || wanted is null
            ? current is null && wanted is null
            : string.Equals(current.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsWellFormed(MediaIdentifierScheme scheme, string value) =>
        scheme == MediaIdentifierScheme.Imdb ? MediaIdentifierRules.IsImdbId(value) : MediaIdentifierRules.IsTmdbId(value);

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A caller's mistake, thrown before anything is sent: an item id Jellyfin never gives (it goes into the path, so
    /// only a Guid is sent, MAM-8's rule), a kind <see cref="WritableKinds"/> leaves out — a series above all (see the
    /// top of this file) —, no edit, an edit of a scheme a movie item can't take — a show's TMDb id never goes onto it
    /// (ADR 0017) — or of one scheme twice, a malformed value, an "only where absent" that sets nothing, or an expected
    /// value without its rule.
    /// </summary>
    private Guid ValidateChange(LibraryIdentifierChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!Guid.TryParse(change.SourceItemId, out var itemId))
            throw new ArgumentException("The item id is not one Jellyfin gives.", nameof(change));
        if (!WritableKinds.Contains(change.Kind))
            throw new ArgumentException(UnwritableKindReason(change.Kind), nameof(change));
        if (change.Edits is not { Count: > 0 })
            throw new ArgumentException("The change has no edit.", nameof(change));

        var schemes = new HashSet<MediaIdentifierScheme>();
        foreach (var edit in change.Edits)
        {
            if (edit is null)
                throw new ArgumentException("The change has an empty edit.", nameof(change));
            if (edit.Scheme is not (MediaIdentifierScheme.Tmdb or MediaIdentifierScheme.Imdb))
                throw new ArgumentException($"A {change.Kind} item takes no {edit.Scheme} id.", nameof(change));
            if (!schemes.Add(edit.Scheme))
                throw new ArgumentException($"The change edits {edit.Scheme} twice.", nameof(change));
            if (!Enum.IsDefined(edit.Rule))
                throw new ArgumentException($"{edit.Rule} is not an edit rule.", nameof(change));
            if (edit.Value is not null && !IsWellFormed(edit.Scheme, edit.Value))
                throw new ArgumentException($"The {edit.Scheme} value is not an id of it.", nameof(change));
            if (edit.Rule == LibraryIdentifierEditRule.OnlyWhereAbsent && edit.Value is null)
                throw new ArgumentException($"An edit of {edit.Scheme} made only where it is absent must set an id.", nameof(change));
            if (edit.Expected is not null
                && (edit.Rule != LibraryIdentifierEditRule.OnlyWhereExpected || !IsWellFormed(edit.Scheme, edit.Expected)))
                throw new ArgumentException($"The expected {edit.Scheme} value belongs to an edit made only where it is expected, and must be an id.", nameof(change));
        }

        return itemId;
    }

    /// <summary>
    /// Whether a transport failure of the POST may have come after the change reached Jellyfin. Only the failures of
    /// making the connection — the name, the connection, its TLS, a proxy tunnel — are certain to have sent nothing;
    /// the rest (the answer cut off, malformed, too large) came once the request was on its way.
    /// </summary>
    private static bool MayHaveArrived(HttpRequestException exception) =>
        exception.HttpRequestError is not (HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError
            or HttpRequestError.ProxyTunnelError);

    /// <summary>The item's path, in Jellyfin's own 32-hex-digit form.</summary>
    private static string ItemPath(Guid itemId) => string.Create(CultureInfo.InvariantCulture, $"Items/{itemId:N}");

    /// <summary><paramref name="write"/> with its sentence masked of the call's secrets: unlike an error, a value is
    /// not masked on its way out (<see cref="HttpIntegrationAdapter.CallAsync{T}"/>).</summary>
    private static LibraryIdentifierWrite Masked(LibraryIdentifierWrite write, IntegrationConnectionSettings settings) =>
        write.Problem is null ? write : write with { Problem = SecretRedaction.Redact(write.Problem, settings.Secrets) };

    /// <summary>The first call's result: the write so far, and the administrator the item is read again as.</summary>
    private sealed record SentChange(LibraryIdentifierWrite Write, string UserId);
}
