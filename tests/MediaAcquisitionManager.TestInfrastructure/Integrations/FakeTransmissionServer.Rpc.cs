using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>MAM-30 — the four methods the fake serves, read from the body once every check passed.</summary>
public sealed partial class FakeTransmissionServer
{
    private const string Success = "success";

    // A method a body doesn't name: both versions; and one Transmission doesn't know, worded by version (4.0.6
    // rpcimpl.cc:2497-2504; 4.1.3 api-compat.cc:393, the legacy translation of JSON-RPC's "method not found").
    private const string NoMethodName = "no method name";
    private const string MethodNameNotRecognized = "method name not recognized";

    private const string NoFieldsSpecified = "no fields specified";

    // The torrent-get fields the fake knows: those MAM-29 recorded, which hold every field the app reads. Transmission
    // leaves out a name it doesn't know; the fake leaves out every other name the same way — real fields it doesn't
    // model too, such as rateDownload, eta or peersConnected, which a real Transmission would answer. So a list naming
    // only such fields is refused on 4.1 and lists each torrent as {} on 4.0, as one naming only made-up fields is.
    private static readonly HashSet<string> TorrentFields = new(StringComparer.Ordinal)
    {
        "addedDate", "doneDate", "downloadDir", "error", "errorString", "fileStats", "files", "hashString", "id", "isFinished",
        "isStalled", "labels", "leftUntilDone", "metadataPercentComplete", "name", "percentDone", "sizeWhenDone", "status", "totalSize",
    };

    // Transmission sends its text as UTF-8, not as \u escapes.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // C's isspace, which tr_strv_strip trims.
    private static readonly char[] Whitespace = [' ', '\t', '\n', '\v', '\f', '\r'];

    /// <summary>
    /// The RPC: the call recorded, then answered by <see cref="RpcIntercept"/> or by its method, the request's integer
    /// <c>tag</c> echoed. A body that isn't a JSON object names no method, as 4.0.6 answers it; 4.1.3 answers a JSON-RPC
    /// parse error instead, whose wording the fake can't know — the app never sends one.
    /// </summary>
    private HttpResponseMessage Rpc(HttpRequestMessage request, JsonObject? envelope)
    {
        if (envelope is null)
            return JsonAnswer(Refused(NoMethodName));

        var call = CallOf(request, envelope);
        lock (_lock)
        {
            _calls.Add(call);
            if (call.Method == "torrent-add")
                _addRequests.Add(AddRequestOf(call.Arguments));
        }

        var answer = RpcIntercept?.Invoke(call)?.DeepClone().AsObject() ?? call.Method switch
        {
            "session-get" => SessionGet(call.Arguments),
            "free-space" => FreeSpace(call.Arguments),
            "torrent-get" => TorrentGet(call.Arguments),
            "torrent-add" => TorrentAdd(call.Arguments),
            "" => Refused(NoMethodName),
            _ => Refused(Version.IsAtLeast41 ? NoMethodName : MethodNameNotRecognized),
        };
        if (call.Tag is { } tag && !answer.ContainsKey("tag"))
            answer["tag"] = tag;
        return JsonAnswer(answer);
    }

    /// <summary>
    /// <c>session-get</c>: the fields asked for that the fake knows, or every one of them when none are asked for —
    /// <c>session-id</c> among them, which a reply with <c>fields</c> carries only when asked. <c>rpc-version-semver</c>
    /// only from 4.0 on.
    /// </summary>
    private JsonObject SessionGet(JsonObject arguments)
    {
        List<(string Name, JsonNode? Value)> fields =
        [
            ("download-dir", DownloadDir),
            ("incomplete-dir", IncompleteDir),
            ("incomplete-dir-enabled", IncompleteDirEnabled),
            ("rename-partial-files", RenamePartialFiles),
            ("rpc-version", Version.RpcVersion),
            ("rpc-version-minimum", Version.RpcVersionMinimum),
            ("rpc-version-semver", Version.RpcVersionSemver),
            ("session-id", SessionId),
            ("start-added-torrents", StartAddedTorrents),
            ("version", Version.LongVersion),
        ];
        var asked = TextsOf(arguments["fields"]);
        var answered = new JsonObject();
        // Only rpc-version-semver can be missing: a version before 4.0 doesn't know it.
        foreach (var (name, value) in fields.Where(field => field.Value is not null && (asked is null || asked.Contains(field.Name))))
            answered[name] = value;
        return Succeeded(answered);
    }

    /// <summary>
    /// <c>free-space</c>: the free and total bytes of a folder the server sees, the total in snake case; for any other
    /// folder statvfs' "No such file or directory" with -1 both — the total in camel case on 4.1, whose JSON-RPC
    /// translation doesn't know a failure's arguments as free-space's (<c>free-space-missing-folder.json</c>).
    /// </summary>
    private JsonObject FreeSpace(JsonObject arguments)
    {
        if (TextOf(arguments, "path") is not { } path)
            return Refused("directory path argument is missing");
        if (IsRelative(path))
            return Refused("directory path is not absolute");

        bool seen;
        (long Free, long Total) space;
        lock (_lock)
        {
            seen = _folders.TryGetValue(FolderKey(path), out space);
        }

        if (seen)
            return Succeeded(new JsonObject { ["path"] = path, ["size-bytes"] = space.Free, ["total_size"] = space.Total });

        return Refused(
            "No such file or directory",
            new JsonObject { ["path"] = path, ["size-bytes"] = -1, [Version.IsAtLeast41 ? "totalSize" : "total_size"] = -1 });
    }

    /// <summary>
    /// <c>torrent-get</c>: the torrents <c>ids</c> names — a list or one alone, each an id, a v1 hash in any letter case
    /// (or base32, or a magnet link naming it), as Transmission looks one up; anything else names none — or every
    /// torrent without <c>ids</c>; each with the fields asked for that the fake knows (<see cref="TorrentFields"/>), in
    /// their order. Without a <c>fields</c> list both versions answer "no fields specified" — 4.0 with the empty
    /// <c>torrents</c> it had already put in its answer (4.0.6 <c>rpcimpl.cc:905-930</c>). A list naming no field the
    /// fake knows is refused the same way on 4.1 (4.1.3 <c>:1027-1030</c>), while 4.0 lists each torrent as <c>{}</c>.
    /// </summary>
    private JsonObject TorrentGet(JsonObject arguments)
    {
        if (TextsOf(arguments["fields"]) is not { } asked)
        {
            return Version.IsAtLeast41
                ? Refused(NoFieldsSpecified)
                : Refused(NoFieldsSpecified, new JsonObject { ["torrents"] = new JsonArray() });
        }

        var fields = asked.Where(TorrentFields.Contains).ToList();
        if (fields.Count == 0 && Version.IsAtLeast41)
            return Refused(NoFieldsSpecified);

        List<FakeTransmissionTorrent> listed;
        lock (_lock)
        {
            if (!arguments.TryGetPropertyValue("ids", out var ids))
                listed = [.. _torrents];
            else if (ids is JsonArray list)
                listed = [.. list.Select(FindLocked).OfType<FakeTransmissionTorrent>()];
            else
                listed = FindLocked(ids) is { } one ? [one] : [];
        }

        var torrents = new JsonArray();
        foreach (var torrent in listed)
        {
            var described = new JsonObject();
            foreach (var field in fields)
                described[field] = FieldOf(torrent, field);
            torrents.Add(described);
        }

        return Succeeded(new JsonObject { ["torrents"] = torrents });
    }

    /// <summary>
    /// <c>torrent-add</c>, in <c>torrentAdd</c>'s order (4.0.6 <c>rpcimpl.cc:1592-1722</c>, 4.1.3 <c>:1746-1882</c>), every
    /// refusal before anything is added: neither <c>filename</c> nor <c>metainfo</c>; a relative <c>download-dir</c>
    /// (an empty one too); a blank label or one with a comma; what can't be read as a torrent Transmission takes —
    /// "unrecognized info", a v2-only torrent and a <c>btmh</c>-only magnet among them. Then a torrent already there is
    /// answered as the duplicate it is, nothing of this add applied (<c>tr_torrentNew</c>, ADR 0039); anything else is
    /// added under the next id, started unless <c>paused</c> says otherwise (or, without it, <see cref="StartAddedTorrents"/>).
    /// <para>
    /// A started add is listed downloading (status 4) at once, a paused one stopped (0). That skips, on purpose, the
    /// verify Transmission runs first on a .torrent whose files aren't all there yet — status 1, then 2 with
    /// <c>isStalled</c> true, then 4 or 0 (<c>on_metainfo_completed</c>: 4.0.6 <c>torrent.cc:1006-1008</c>, 4.1.3
    /// <c>:874-879</c>) — as the fake has no time to pass. Real 4.0.6, read straight after its add, was still verifying
    /// (<see cref="TransmissionSamples.TorrentGetVerifying406File"/>); 4.1.3, read later, was downloading. A test that
    /// wants the verify plants it (<see cref="UpdateTorrent"/>, <see cref="TransmissionSamples.BigBuckBunnyVerifying"/>).
    /// </para>
    /// </summary>
    private JsonObject TorrentAdd(JsonObject arguments)
    {
        var filename = TextOf(arguments, "filename") ?? "";
        var metainfo = TextOf(arguments, "metainfo") ?? "";
        if (filename.Length == 0 && metainfo.Length == 0)
            return Refused("no filename or metainfo specified");

        var downloadDir = TextOf(arguments, "download-dir");
        if (downloadDir is not null && IsRelative(downloadDir))
            return Refused("download directory path is not absolute");

        // make_labels: each text label trimmed, refused blank or with a comma, anything else in the list ignored; then
        // set_labels keeps each once, in order, letter case kept.
        var labels = new List<string>();
        foreach (var text in TextsOf(arguments["labels"]) ?? [])
        {
            var label = text.Trim(Whitespace);
            if (label.Length == 0)
                return Refused("labels cannot be empty");
            if (label.Contains(',', StringComparison.Ordinal))
                return Refused("labels cannot contain comma (,) character");
            if (!labels.Contains(label, StringComparer.Ordinal))
                labels.Add(label);
        }

        // A filename is read first: a local .torrent Transmission's disk would have, which the fake has none of, or —
        // as the app sends it — a magnet link.
        var added = filename.Length > 0 ? FromMagnet(filename) : FromMetainfo(metainfo);
        if (added is null)
            return Refused("unrecognized info");

        var paused = arguments["paused"] is JsonValue flag && flag.TryGetValue<bool>(out var value) ? value : (bool?)null;
        var now = Now();
        lock (_lock)
        {
            if (_torrents.Find(torrent => string.Equals(torrent.HashString, added.HashString, StringComparison.OrdinalIgnoreCase)) is { } duplicate)
                return Succeeded(new JsonObject { ["torrent-duplicate"] = Brief(duplicate) });

            var torrent = added with
            {
                Id = _nextId++,
                DownloadDir = downloadDir ?? DownloadDir,
                Labels = labels,
                Status = (paused is { } stopped ? !stopped : StartAddedTorrents) ? 4 : 0,
                AddedDate = now,
            };
            _torrents.Add(torrent);
            return Succeeded(new JsonObject { ["torrent-added"] = Brief(torrent) });
        }
    }

    /// <summary>The torrent <paramref name="id"/> names: a number its id, a text its hash; under the lock.</summary>
    private FakeTransmissionTorrent? FindLocked(JsonNode? id)
    {
        if (id is not JsonValue value)
            return null;
        if (value.TryGetValue<long>(out var number))
            return _torrents.Find(torrent => torrent.Id == number);
        return value.TryGetValue<string>(out var text) && HashOf(text) is { } hash
            ? _torrents.Find(torrent => string.Equals(torrent.HashString, hash, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>One field of a torrent as the version writes it: 4.1 adds <c>begin_piece</c> and <c>end_piece</c> to
    /// each file, and the two fractions are written by the version's rule (<see cref="Fraction"/>).</summary>
    private JsonNode? FieldOf(FakeTransmissionTorrent torrent, string field) => field switch
    {
        "addedDate" => torrent.AddedDate,
        "doneDate" => torrent.DoneDate,
        "downloadDir" => torrent.DownloadDir,
        "error" => torrent.Error,
        "errorString" => torrent.ErrorString,
        "fileStats" => new JsonArray([.. torrent.Files.Select(file => (JsonNode)new JsonObject
        {
            ["bytesCompleted"] = file.BytesCompleted,
            ["priority"] = file.Priority,
            ["wanted"] = file.Wanted,
        })]),
        "files" => FilesOf(torrent),
        "hashString" => torrent.HashString,
        "id" => torrent.Id,
        "isFinished" => torrent.IsFinished,
        "isStalled" => torrent.IsStalled,
        "labels" => new JsonArray([.. torrent.Labels.Select(label => (JsonNode)JsonValue.Create(label))]),
        "leftUntilDone" => torrent.LeftUntilDone,
        "metadataPercentComplete" => Fraction(torrent.MetadataPercentComplete),
        "name" => torrent.Name,
        "percentDone" => Fraction(torrent.PercentDone),
        "sizeWhenDone" => torrent.SizeWhenDone,
        "status" => torrent.Status,
        "totalSize" => torrent.TotalSize,
        _ => throw new UnreachableException($"torrent-get has no field {field}."),
    };

    /// <summary>
    /// <c>files</c>: each file's name and sizes; on 4.1 also the pieces it spans, <c>end_piece</c> past its last — an
    /// empty file the one it starts in.
    /// </summary>
    private JsonArray FilesOf(FakeTransmissionTorrent torrent)
    {
        var files = new JsonArray();
        long offset = 0;
        foreach (var file in torrent.Files)
        {
            var described = new JsonObject();
            if (Version.IsAtLeast41)
                described["begin_piece"] = offset / torrent.PieceLength;
            described["bytesCompleted"] = file.BytesCompleted;
            if (Version.IsAtLeast41)
                described["end_piece"] = ((offset + Math.Max(file.Length, 1) - 1) / torrent.PieceLength) + 1;
            described["length"] = file.Length;
            described["name"] = file.Name;
            files.Add(described);
            offset += file.Length;
        }

        return files;
    }

    /// <summary>
    /// A fraction as the version writes it: 4.1's JSON writer gives a whole one a decimal point (<c>1.0</c>), 4.0's
    /// doesn't (<c>1</c>) — written as raw JSON, which .NET's writer alone can't keep apart.
    /// </summary>
    private JsonNode Fraction(double value) =>
        JsonNode.Parse(Version.IsAtLeast41 && double.IsInteger(value)
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture))!;

    /// <summary>What <c>torrent-added</c> and <c>torrent-duplicate</c> say of a torrent: these three, both versions.</summary>
    private static JsonObject Brief(FakeTransmissionTorrent torrent) => new()
    {
        ["hashString"] = torrent.HashString,
        ["id"] = torrent.Id,
        ["name"] = torrent.Name,
    };

    private static FakeTransmissionAddRequest AddRequestOf(JsonObject arguments)
    {
        var filename = TextOf(arguments, "filename");
        var metainfo = TextOf(arguments, "metainfo");
        return new FakeTransmissionAddRequest(
            [.. arguments.Select(argument => argument.Key)],
            !string.IsNullOrEmpty(filename) ? ReleasePayloadKind.Magnet
                : !string.IsNullOrEmpty(metainfo) ? ReleasePayloadKind.TorrentFile
                : null,
            metainfo is null ? null : Base64(metainfo),
            filename,
            TextOf(arguments, "download-dir"),
            TextsOf(arguments["labels"]),
            arguments["paused"] is JsonValue flag && flag.TryGetValue<bool>(out var paused) ? paused : null);
    }

    private static JsonObject Succeeded(JsonObject arguments) => new() { ["arguments"] = arguments, ["result"] = Success };

    /// <summary>A refusal: HTTP 200 all the same, its sentence in <c>result</c>, and arguments <c>{}</c> unless the
    /// method keeps some.</summary>
    private static JsonObject Refused(string result, JsonObject? arguments = null) =>
        new() { ["arguments"] = arguments ?? new JsonObject(), ["result"] = result };

    private HttpResponseMessage JsonAnswer(JsonObject answer)
    {
        var response = Stamp(new HttpResponseMessage(HttpStatusCode.OK), pastAddressCheck: true);
        response.Content = new StringContent(answer.ToJsonString(Json), Encoding.UTF8);
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=UTF-8");
        return response;
    }

    /// <summary>The texts of <paramref name="node"/> when it is a list, anything else in it left out; <c>null</c> when it
    /// is no list.</summary>
    private static List<string>? TextsOf(JsonNode? node) =>
        node is JsonArray list
            ? [.. list.OfType<JsonValue>().Select(item => item.TryGetValue<string>(out var text) ? text : null).OfType<string>()]
            : null;

    /// <summary>tr_sys_path_is_relative on Linux: anything not starting with <c>/</c>, an empty path included.</summary>
    private static bool IsRelative(string path) => path.Length == 0 || path[0] != '/';

    private static byte[]? Base64(string text)
    {
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The v1 hash <paramref name="text"/> names as <c>parseMagnet</c> reads it: trimmed, a bare hash — 40 hexadecimal
    /// characters in any case, or 32 base32 — or a magnet link's <c>urn:btih:</c> topic; <c>null</c> for anything else,
    /// a link with only a v2 <c>urn:btmh:</c> topic among them.
    /// </summary>
    private static string? HashOf(string text)
    {
        var trimmed = text.Trim(Whitespace);
        if (trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            return TorrentInfoHashReader.FromMagnet(trimmed).V1;
        return trimmed.Length is 40 or 32 && trimmed.All(char.IsAsciiLetterOrDigit)
            ? TorrentInfoHashReader.FromMagnet("magnet:?xt=urn:btih:" + trimmed).V1
            : null;
    }
}
