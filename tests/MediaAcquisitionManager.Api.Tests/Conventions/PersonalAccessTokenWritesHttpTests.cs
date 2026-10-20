using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

// MAM-5 — what a personal access token may call, swept over the running app's endpoints rather than a hand-kept list
// (ADR 0040): every GET a user can reach and two POSTs that only read; with Read and download, three writes more; every
// other write — one added later included — refuses any token, the owner's too, with 403 and a sentence, before
// anything is bound, looked up or written; the account, token management and every settings action are refused even
// as reads. A refusal is a Warning every time and an audit row at most once a minute per token. The marks themselves
// are pinned as exact sets, so opening or closing an endpoint to tokens has to change this file.
public sealed class PersonalAccessTokenWritesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // Spelled out rather than read from the convention: this pins where settings controllers live.
    private const string SettingsNamespace = "MediaAcquisitionManager.Api.Controllers.Settings";

    // An unmarked write outside every owner-only convention, which only the token check refuses: the owner's Find
    // recommendations.
    private const string UnmarkedWriteUrl = "/api/recommendations/runs";

    // Spelled out rather than read from PersonalAccessTokenErrors: these are what a script's author reads.
    private const string NotForTokensError =
        "An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README.";

    private const string ReadOnlyError =
        "This access token is read only. To send downloads, cancel following or mark Not interested from a script, create a token with “Read and download” access on your Profile page; see “Scripts and the API” in the README.";

    private const string ReadAndDownloadTokenName = "Claude on the laptop";
    private const string ReadOnlyTokenName = "Library report";

    // Far longer than a refusal takes, far shorter than the 30 s a request waiting for SQLite's write lock would give up
    // after.
    private static readonly TimeSpan WriteLockWait = TimeSpan.FromSeconds(10);

    // ADR 0040 — the three writes Read and download opens.
    private static readonly string[] WritesOpenToReadAndDownload =
    [
        "POST api/downloads",
        "POST api/downloads/{id:guid}/cancel",
        "PUT api/recommendations/dismissals/{kind}/{catalogId}",
    ];

    // ADR 0040 — the two POSTs that only read, open to every token.
    private static readonly string[] PostsThatOnlyRead =
    [
        "POST api/releases/search",
        "POST api/searches",
    ];

    // ADR 0040 — refused to every token outside the settings namespace, reads included: the account's own session,
    // its password, and the token management.
    private static readonly string[] RefusedOutsideSettings =
    [
        "DELETE api/profile/tokens/{id:guid}",
        "GET api/auth/me",
        "GET api/profile/tokens",
        "POST api/auth/password",
        "POST api/profile/tokens",
    ];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private IssuedPersonalAccessToken _readAndDownload = null!;
    private IssuedPersonalAccessToken _readOnly = null!;

    public PersonalAccessTokenWritesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        var tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        // The owner's tokens: no owner-only gate can be what refuses them, only the token check.
        _owner = await users.CreateAuthorizedOwner("owner");
        var member = await users.CreateAuthorized("member");
        _readAndDownload = await tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload, name: ReadAndDownloadTokenName);
        _readOnly = await tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadOnly, name: ReadOnlyTokenName);
        // Rows a write that got through could change: another user, their session and their token.
        await tokens.Create(member.User, PersonalAccessTokenAccess.ReadAndDownload);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Discovery_finds_exactly_the_three_writes_open_to_read_and_download()
    {
        //Arrange

        //Act
        var marked = Operations()
            .Where(operation => IsWrite(operation.Method) && Has<PersonalAccessTokenWriteAttribute>(operation.Endpoint))
            .Select(operation => operation.Description)
            .Order(StringComparer.Ordinal);

        //Assert
        Assert.Equal(WritesOpenToReadAndDownload, marked);
    }

    [Fact]
    public void Discovery_finds_exactly_the_two_posts_that_only_read()
    {
        //Arrange

        //Act
        var marked = Operations()
            .Where(operation => Has<PersonalAccessTokenReadAttribute>(operation.Endpoint))
            .Select(operation => operation.Description)
            .Order(StringComparer.Ordinal);

        //Assert
        Assert.Equal(PostsThatOnlyRead, marked);
    }

    [Fact]
    public void Discovery_finds_the_refusal_on_the_account_the_token_management_and_nothing_else_outside_settings()
    {
        //Arrange

        //Act
        var refused = Operations()
            .Where(operation => !IsSettingsEndpoint(operation.Endpoint) && Has<DenyPersonalAccessTokenAttribute>(operation.Endpoint))
            .Select(operation => operation.Description)
            .Order(StringComparer.Ordinal);

        //Assert
        Assert.Equal(RefusedOutsideSettings, refused);
    }

    [Fact]
    public void Discovery_finds_the_refusal_on_every_settings_action_reads_included()
    {
        //Arrange

        //Act
        var settings = Operations().Where(operation => IsSettingsEndpoint(operation.Endpoint)).ToList();

        //Assert
        // Guards the guard: a discovery that misses these would let the check below pass for less, or for nothing.
        Assert.Superset(
            new HashSet<string>
            {
                "GET api/settings/users",
                "GET api/settings/users/{id:guid}",
                "DELETE api/settings/users/{id:guid}/tokens",
                "GET api/settings/integrations",
                "POST api/settings/integrations",
                "GET api/settings/allowed-roots",
                "PUT api/settings/allowed-roots",
                "GET api/settings/destinations",
                "GET api/settings/destinations/client-destinations/{downloadClientId:guid}",
            },
            settings.Select(operation => operation.Description).ToHashSet());
        Assert.All(settings, operation =>
            Assert.True(Has<DenyPersonalAccessTokenAttribute>(operation.Endpoint), $"{operation.Description} lets a token in."));
    }

    // ADR 0040 — deny by default, swept with each of the owner's tokens: Read and download, the widest a token gets, and
    // Read only, so neither access has a path of its own past the refusal. Every [Authorize] write but the three is
    // refused before binding and lookups (no body, route values nobody has), and the refusals of one token make one audit
    // row, however many there were in the minute.
    [Theory]
    [InlineData(PersonalAccessTokenAccess.ReadOnly)]
    [InlineData(PersonalAccessTokenAccess.ReadAndDownload)]
    public async Task Every_other_write_with_an_owners_token_is_403_with_the_sentence_and_changes_nothing(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var (token, tokenName) = access == PersonalAccessTokenAccess.ReadOnly
            ? (_readOnly, ReadOnlyTokenName)
            : (_readAndDownload, ReadAndDownloadTokenName);
        var writes = Operations()
            .Where(operation => IsWrite(operation.Method)
                && !IsAnonymous(operation.Endpoint)
                && !Has<PersonalAccessTokenWriteAttribute>(operation.Endpoint)
                && !Has<PersonalAccessTokenReadAttribute>(operation.Endpoint))
            .ToList();
        var rowsBefore = await CountRowsAsync(cancellationToken);
        var accountsBefore = await AccountsAsync(cancellationToken);

        //Act
        var answers = new List<(string Write, string Url, HttpStatusCode Status, string? ContentType, string? Error)>();
        foreach (var write in writes)
        {
            var url = UrlFor(write.Endpoint.RoutePattern);
            var response = await SendAsync(write.Method, url, token.Secret);
            answers.Add((
                write.Description,
                url,
                response.StatusCode,
                response.Content.Headers.ContentType?.MediaType,
                await ReadErrorOrNullAsync(response, cancellationToken)));
        }

        //Assert
        // Guards the guard: the sweep reaches the writes of every area, the account's and the settings' too.
        Assert.True(answers.Count >= 40, $"Only {answers.Count} writes were swept.");
        Assert.Superset(
            new HashSet<string>
            {
                "POST api/auth/password",
                "POST api/profile/tokens",
                "DELETE api/profile/tokens/{id:guid}",
                "PUT api/profile/recommendations",
                // MAM-46 — the user's own language for the web app.
                "PUT api/profile/language",
                "DELETE api/recommendations/dismissals/{kind}/{catalogId}",
                "POST api/recommendations/runs",
                "POST api/library/scans",
                "POST api/replacements",
                "POST api/settings/users",
                "DELETE api/settings/users/{id:guid}/tokens",
                "PUT api/settings/allowed-roots",
            },
            answers.Select(answer => answer.Write).ToHashSet());
        Assert.All(answers, answer =>
        {
            Assert.True(answer.Status == HttpStatusCode.Forbidden, $"{answer.Write} answered {(int)answer.Status}.");
            Assert.Equal("application/json", answer.ContentType);
            Assert.Equal(NotForTokensError, answer.Error);
        });
        Assert.Equal(RowsWithout(rowsBefore, "AuditEvents"), RowsWithout(await CountRowsAsync(cancellationToken), "AuditEvents"));
        Assert.Equal(accountsBefore, await AccountsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            // One row for the whole sweep: at most one per token a minute.
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.PersonalAccessTokenRefused, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.PersonalAccessToken, audit.TargetKind);
            Assert.Equal(token.Token.Id, audit.TargetId);
            Assert.Equal(tokenName, audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
        // And a Warning for every refusal, naming what was refused.
        var warnings = RefusalWarnings();
        Assert.Equal(answers.Count, warnings.Count);
        Assert.All(answers, answer =>
        {
            var method = answer.Write[..answer.Write.IndexOf(' ', StringComparison.Ordinal)];
            Assert.Single(warnings, warning => warning.Message.Contains($" was refused {method} {answer.Url}: NotForTokens.", StringComparison.Ordinal));
        });
        Assert.All(warnings, warning => Assert.Contains(token.Token.Id.ToString(), warning.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_three_writes_with_the_owners_read_only_token_are_403_with_the_read_only_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var writes = Operations()
            .Where(operation => IsWrite(operation.Method) && Has<PersonalAccessTokenWriteAttribute>(operation.Endpoint))
            .ToList();
        var rowsBefore = await CountRowsAsync(cancellationToken);

        //Act
        var answers = new List<(string Write, HttpStatusCode Status, string? ContentType, string? Error)>();
        foreach (var write in writes)
        {
            var response = await SendAsync(write.Method, UrlFor(write.Endpoint.RoutePattern), _readOnly.Secret);
            answers.Add((
                write.Description,
                response.StatusCode,
                response.Content.Headers.ContentType?.MediaType,
                await ReadErrorOrNullAsync(response, cancellationToken)));
        }

        //Assert
        Assert.Equal(WritesOpenToReadAndDownload.Length, answers.Count);
        Assert.All(answers, answer =>
        {
            Assert.True(answer.Status == HttpStatusCode.Forbidden, $"{answer.Write} answered {(int)answer.Status}.");
            Assert.Equal("application/json", answer.ContentType);
            Assert.Equal(ReadOnlyError, answer.Error);
        });
        Assert.Equal(RowsWithout(rowsBefore, "AuditEvents"), RowsWithout(await CountRowsAsync(cancellationToken), "AuditEvents"));
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.PersonalAccessTokenRefused, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal(_readOnly.Token.Id, audit.TargetId);
            Assert.Equal(ReadOnlyTokenName, audit.TargetName);
        });
        Assert.Equal(answers.Count, RefusalWarnings().Count(warning => warning.Message.EndsWith(": ReadOnly.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_three_writes_with_a_read_and_download_token_are_not_refused_as_a_token()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var writes = Operations()
            .Where(operation => IsWrite(operation.Method) && Has<PersonalAccessTokenWriteAttribute>(operation.Endpoint))
            .ToList();

        //Act
        var answers = new List<(string Write, HttpStatusCode Status)>();
        foreach (var write in writes)
            answers.Add((write.Description, (await SendAsync(write.Method, UrlFor(write.Endpoint.RoutePattern), _readAndDownload.Secret)).StatusCode));

        //Assert
        // Guards the guard: the 403s above come from the token check, not from endpoints that refuse every caller. The
        // empty body and the ids nobody has get the endpoint's own answer (a 400 or a 404).
        Assert.Equal(WritesOpenToReadAndDownload.Length, answers.Count);
        Assert.All(answers, answer =>
            Assert.True(answer.Status is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"{answer.Write} answered {(int)answer.Status}."));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenRefused, cancellationToken)));
        Assert.Empty(RefusalWarnings());
    }

    [Fact]
    public async Task The_two_posts_that_only_read_with_a_read_only_token_are_not_refused_as_a_token()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var posts = Operations().Where(operation => Has<PersonalAccessTokenReadAttribute>(operation.Endpoint)).ToList();

        //Act
        var answers = new List<(string Post, HttpStatusCode Status)>();
        foreach (var post in posts)
            answers.Add((post.Description, (await SendAsync(post.Method, UrlFor(post.Endpoint.RoutePattern), _readOnly.Secret)).StatusCode));

        //Assert
        // The empty body gets the endpoint's own 400: the token got past the check.
        Assert.Equal(PostsThatOnlyRead.Length, answers.Count);
        Assert.All(answers, answer =>
            Assert.True(answer.Status is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"{answer.Post} answered {(int)answer.Status}."));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenRefused, cancellationToken)));
        Assert.Empty(RefusalWarnings());
    }

    // ADR 0040 — refused even though they read: every settings read (Users, Integrations, Media folders, Destinations),
    // the account's session and the token list, to the owner's tokens of either access.
    [Theory]
    [InlineData(PersonalAccessTokenAccess.ReadOnly)]
    [InlineData(PersonalAccessTokenAccess.ReadAndDownload)]
    public async Task Every_refused_read_with_an_owners_token_is_403_with_the_sentence(PersonalAccessTokenAccess access)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = access == PersonalAccessTokenAccess.ReadOnly ? _readOnly : _readAndDownload;
        var reads = Operations()
            .Where(operation => !IsWrite(operation.Method) && Has<DenyPersonalAccessTokenAttribute>(operation.Endpoint))
            .ToList();

        //Act
        var answers = new List<(string Read, HttpStatusCode Status, string? ContentType, string? Error)>();
        foreach (var read in reads)
        {
            var response = await SendAsync(read.Method, UrlFor(read.Endpoint.RoutePattern), token.Secret);
            answers.Add((
                read.Description,
                response.StatusCode,
                response.Content.Headers.ContentType?.MediaType,
                await ReadErrorOrNullAsync(response, cancellationToken)));
        }

        //Assert
        // Guards the guard: every settings read of today, the account's session and the token list.
        Assert.Superset(
            new HashSet<string>
            {
                "GET api/auth/me",
                "GET api/profile/tokens",
                "GET api/settings/users",
                "GET api/settings/users/{id:guid}",
                "GET api/settings/integrations",
                "GET api/settings/integrations/adapter-types",
                "GET api/settings/integrations/{id:guid}",
                "GET api/settings/allowed-roots",
                "GET api/settings/destinations",
                "GET api/settings/destinations/{id:guid}",
                "GET api/settings/destinations/client-destinations/{downloadClientId:guid}",
                "GET api/settings/destinations/sections",
            },
            answers.Select(answer => answer.Read).ToHashSet());
        Assert.All(answers, answer =>
        {
            Assert.True(answer.Status == HttpStatusCode.Forbidden, $"{answer.Read} answered {(int)answer.Status}.");
            Assert.Equal("application/json", answer.ContentType);
            Assert.Equal(NotForTokensError, answer.Error);
        });
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.PersonalAccessTokenRefused, audit.Action);
            Assert.Equal(token.Token.Id, audit.TargetId);
        });
        Assert.Equal(answers.Count, RefusalWarnings().Count);
    }

    // ADR 0040 — a script looping on a refused write fills no audit table: one row per token a minute, each refusal
    // still a Warning.
    [Fact]
    public async Task Refusals_of_one_token_make_one_audit_row_a_minute_and_a_warning_each()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var first = await SendAsync("POST", UnmarkedWriteUrl, _readAndDownload.Secret);
        _factory.Time.Advance(TimeSpan.FromSeconds(59));
        var withinTheMinute = await SendAsync("POST", UnmarkedWriteUrl, _readAndDownload.Secret);
        var rowsWithinTheMinute = await RefusedRowsAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var secondUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var aMinuteLater = await SendAsync("POST", UnmarkedWriteUrl, _readAndDownload.Secret);
        var rowsAMinuteLater = await RefusedRowsAsync(cancellationToken);

        //Assert
        Assert.All(new[] { first, withinTheMinute, aMinuteLater }, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        Assert.Equal([firstUtc], rowsWithinTheMinute.Select(row => row.CreatedUtc));
        Assert.Equal([firstUtc, secondUtc], rowsAMinuteLater.Select(row => row.CreatedUtc));
        Assert.All(rowsAMinuteLater, row =>
        {
            Assert.Equal(_owner.User.Id, row.ActorUserId);
            Assert.Equal(AuditTargetKind.PersonalAccessToken, row.TargetKind);
            Assert.Equal(_readAndDownload.Token.Id, row.TargetId);
        });
        await _factory.WithDbAsync(async db =>
        {
            var token = await db.PersonalAccessTokens.SingleAsync(row => row.Id == _readAndDownload.Token.Id, cancellationToken);
            Assert.Equal(secondUtc, token.LastRefusedUtc);
        });
        var warnings = RefusalWarnings();
        Assert.Equal(3, warnings.Count);
        Assert.All(warnings, warning =>
        {
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Equal(
                $"Personal access token {_readAndDownload.Token.Id} of user {_owner.User.Id} was refused POST {UnmarkedWriteUrl}: NotForTokens.",
                warning.Message);
        });
    }

    // ADR 0040 — whether the minute's audit row is due is read first, outside any transaction: a script refused in a loop
    // is answered at once while another connection writes, not after waiting for SQLite's one writer, and its refusals
    // within the minute still write nothing but their Warning.
    [Fact]
    public async Task Refusal_within_the_minute_answers_while_another_connection_holds_the_write_lock()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var first = await SendAsync("POST", UnmarkedWriteUrl, _readAndDownload.Secret);
        _factory.Time.Advance(TimeSpan.FromSeconds(30));

        //Act
        HttpResponseMessage? whileLocked = null;
        await _factory.WithDbAsync(async db =>
        {
            // BEGIN IMMEDIATE: the write lock, held until the request has answered.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            whileLocked = await SendAsync("POST", UnmarkedWriteUrl, _readAndDownload.Secret).WaitAsync(WriteLockWait, cancellationToken);
        });

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, whileLocked?.StatusCode);
        Assert.Equal(NotForTokensError, await ReadErrorOrNullAsync(whileLocked!, cancellationToken));
        Assert.Equal([firstUtc], (await RefusedRowsAsync(cancellationToken)).Select(row => row.CreatedUtc));
        Assert.Equal(2, RefusalWarnings().Count);
    }

    // Every operation of the app's own controllers as the running app routes them: each method an endpoint answers, or
    // POST for one that answers every method (fail closed, as the token check counts it). Probe controllers aren't the
    // app's surface.
    private List<(RouteEndpoint Endpoint, string Method, string Description)> Operations() =>
        _factory.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly == typeof(Program).Assembly)
            .SelectMany(endpoint => MethodsOf(endpoint)
                .Select(method => (endpoint, method, $"{method} {endpoint.RoutePattern.RawText}")))
            .ToList();

    private static IEnumerable<string> MethodsOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods ? methods : [HttpMethods.Post];

    private static bool IsWrite(string method) => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method);

    private static bool IsAnonymous(RouteEndpoint endpoint) => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    private static bool Has<TAttribute>(RouteEndpoint endpoint) where TAttribute : class =>
        endpoint.Metadata.GetMetadata<TAttribute>() is not null;

    private static bool IsSettingsEndpoint(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(SettingsNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(SettingsNamespace + ".", StringComparison.Ordinal));

    // The route with every parameter filled in: a new Guid for a guid, 1 for a number, a word otherwise.
    private static string UrlFor(RoutePattern pattern) =>
        "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(TextOf))));

    private static string TextOf(RoutePatternPart part) => part switch
    {
        RoutePatternLiteralPart literal => literal.Content,
        RoutePatternSeparatorPart separator => separator.Content,
        RoutePatternParameterPart parameter when HasConstraint(parameter, "guid") => Guid.NewGuid().ToString(),
        RoutePatternParameterPart parameter when HasConstraint(parameter, "int") || HasConstraint(parameter, "long") => "1",
        RoutePatternParameterPart => "sample",
        _ => throw new InvalidOperationException($"Unexpected route part {part}."),
    };

    private static bool HasConstraint(RoutePatternParameterPart parameter, string name) =>
        parameter.ParameterPolicies.Any(policy => string.Equals(policy.Content, name, StringComparison.OrdinalIgnoreCase));

    private Task<HttpResponseMessage> SendAsync(string method, string url, string token)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _factory.SendHttpRequest(request);
    }

    // The error sentence of a { "error": … } body, or null for any other body — so a sweep reports which write answered
    // what instead of stopping at the first body that isn't one. MAM-47 — in the pseudo pass the sentence must be one
    // rendered message, read as its English, as ReadErrorAsync reads it.
    private static async Task<string?> ReadErrorOrNullAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.GetString() is not { } sentence)
                return null;
            return AcceptLanguageHandler.AskedForPseudo(response.RequestMessage) ? PseudoText.Unwrap(sentence, "the error body") : sentence;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private List<RecordedLogEntry> RefusalWarnings() =>
        _factory.Logs.Entries
            .Where(entry => entry.Category == typeof(PersonalAccessTokenRefusals).FullName && entry.Level == LogLevel.Warning)
            .ToList();

    private async Task<List<AuditEventEntity>> RefusedRowsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<AuditEventEntity>();
        await _factory.WithDbAsync(async db => rows = await db.AuditEvents.AsNoTracking()
            .Where(audit => audit.Action == AuditAction.PersonalAccessTokenRefused)
            .OrderBy(audit => audit.CreatedUtc)
            .ToListAsync(cancellationToken));
        return rows;
    }

    // Every table's row count, read through the model, so a table added later is counted too: a write that got through
    // and started a run, saved a setting or made a job would show here.
    private async Task<SortedDictionary<string, long>> CountRowsAsync(CancellationToken cancellationToken)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await _factory.WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            // The table names come from the model, never from a request.
            foreach (var table in db.Model.GetEntityTypes().Select(type => type.GetTableName()).OfType<string>().Distinct())
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
                counts[table] = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }
        });
        return counts;
    }

    private static SortedDictionary<string, long> RowsWithout(SortedDictionary<string, long> counts, string table) =>
        new(counts.Where(count => count.Key != table).ToDictionary(count => count.Key, count => count.Value), StringComparer.Ordinal);

    // What a write to the accounts would change: who exists, their password and state, their sessions, their tokens.
    // "Last used" and "last refused" are left out: a request let in, or refused, writes them on purpose.
    private async Task<string> AccountsAsync(CancellationToken cancellationToken)
    {
        var snapshot = "";
        await _factory.WithDbAsync(async db =>
        {
            var users = await db.Users.AsNoTracking()
                .OrderBy(user => user.UserName)
                .Select(user => new { user.Id, user.UserName, user.PasswordHash, user.IsDisabled, user.LastSignInUtc, user.Language })
                .ToListAsync(cancellationToken);
            var sessions = await db.UserSessions.AsNoTracking()
                .OrderBy(session => session.CreatedUtc).ThenBy(session => session.RefreshTokenHash)
                .Select(session => new { session.Id, session.UserId, session.RefreshTokenHash, session.ExpiresUtc, session.RevokedUtc })
                .ToListAsync(cancellationToken);
            var tokens = await db.PersonalAccessTokens.AsNoTracking()
                .OrderBy(token => token.TokenHash)
                .Select(token => new { token.Id, token.UserId, token.Name, token.Access, token.ExpiresUtc, token.RevokedUtc })
                .ToListAsync(cancellationToken);
            snapshot = JsonSerializer.Serialize(new { users, sessions, tokens });
        });
        return snapshot;
    }
}
