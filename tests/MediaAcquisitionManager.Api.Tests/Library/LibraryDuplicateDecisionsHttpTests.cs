using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-21 — the owner's Confirm, Dismiss and Undo of a possible match (PUT and DELETE
// /api/library/duplicates/{key}/decision; Vasyl's decisions of 2026-10-07, ADR 0029): the installation owner's alone,
// members read the decision without the decider's name; one decision per group key, each change with an audit row
// whose target is the key as a Guid, the same decision again and an undo of nothing writing nothing. A dismissed match
// leaves the list, every class's tab included, and is listed with ?decision=Dismissed; a confirmed one stays, shown as
// confirmed, still needing review. Only the group a key names now is decided — a bare 404 for a key no group has, of
// any shape. A decision never follows a group whose members changed, and never touches the library. The list's
// decision filter sentences are LibraryDuplicatesHttpTests'; MAM-40 — keeping copies as they are, and the classes that
// refuse a decision, LibraryDuplicateKeepBothHttpTests'.
public sealed class LibraryDuplicateDecisionsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/duplicates";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>A second Jellyfin, beside the factory's default address.</summary>
    private const string BedroomBaseUrl = "http://127.0.0.2:9";

    /// <summary>A third, for a work that joins a group.</summary>
    private const string AtticBaseUrl = "http://127.0.0.3:9";

    private const string AlienImdbId = "tt0078748";

    /// <summary>A well-formed key no group has.</summary>
    private const string UnknownKey = "0123456789abcdef0123456789abcdef";

    // Spelled out rather than read from the product code: these are what the owner reads on the page.
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";
    private const string DecisionMissingError = "A decision is needed: Confirmed or Dismissed.";
    private const string UnreadableBodyError = "The body must be JSON with a decision, such as { \"decision\": \"Confirmed\" }.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IMediaWorkDataFactory _works = null!;
    private IMediaVersionDataFactory _versions = null!;
    private IPhysicalFileDataFactory _physicalFiles = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;
    private IntegrationConnectionEntity _bedroom = null!;

    public LibraryDuplicateDecisionsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> DecisionsThatArentOne => new()
    {
        { """{"decision":"Maybe"}""", NotADecision("Maybe") },
        // By name only: the number of a decision is not its name.
        { """{"decision":"1"}""", NotADecision("1") },
        { """{"decision":"Confirmed,Dismissed"}""", NotADecision("Confirmed,Dismissed") },
        // A pasted page of text comes back cut, never whole.
        { $$"""{"decision":"{{new string('x', 500)}}"}""", NotADecision(new string('x', 32) + "…") },
        { """{"decision":"   "}""", DecisionMissingError },
        { """{"decision":null}""", DecisionMissingError },
        { "{}", DecisionMissingError },
        { "null", DecisionMissingError },
        // No body at all.
        { "", DecisionMissingError },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        _works = new MediaWorkDataFactory(db, _factory.Time);
        _versions = new MediaVersionDataFactory(db, _factory.Time);
        _physicalFiles = new PhysicalFileDataFactory(db, _factory.Time);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Two Jellyfins over one disk, with no path mapping: the app looks for each file where its library says it is.
        _livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _bedroom = await _integrations.Create("jellyfin", "Bedroom", BedroomBaseUrl, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Decide_confirmed_is_204_and_stores_the_decision_with_an_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await DecideAsync(key, "Confirmed", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = await SingleDecisionAsync(cancellationToken);
        Assert.Equal(
            (key, DuplicateMatchDecision.Confirmed, (Guid?)_owner.User.Id, "owner", now, now),
            (row.GroupKey, row.Decision, row.DecidedByUserId, row.DecidedByUserName, row.DecidedUtc, row.CreatedUtc));
        // The audit row's target is the group by its key — 32 hex digits read as a Guid — named by the title and year
        // it was offered by.
        var audit = Assert.Single(await AuditAsync(cancellationToken));
        Assert.Equal(
            (AuditAction.DuplicateMatchConfirmed, (Guid?)_owner.User.Id, "owner", (AuditTargetKind?)AuditTargetKind.DuplicateMatch, (Guid?)Guid.ParseExact(key, "N"), "Arrival (2016)", now),
            (audit.Action, audit.ActorUserId, audit.ActorUserName, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
        Assert.Equal(key, audit.TargetId!.Value.ToString("N"));
    }

    [Fact]
    public async Task List_shows_a_confirmed_match_as_confirmed_still_needing_review_with_the_deciders_name_for_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        await PlantPossibleMatchAsync("Brazil", 1985);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await DecideAsync(key, "Confirmed", _owner.AccessToken);

        //Act
        var owners = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken), cancellationToken);
        var members = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _member.AccessToken), cancellationToken);
        var confirmed = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?decision=Confirmed", _member.AccessToken), cancellationToken);

        //Assert
        // Confirmed stays on the list, shown as confirmed: the works stay two, and the group still needs review.
        Assert.Equal(["Arrival", "Brazil"], owners.Items.Select(TitleOf));
        var ownersGroup = owners.Items.Single(group => group.Key == key);
        Assert.Equal((DuplicateClass.Possible, true), (ownersGroup.Class, ownersGroup.NeedsReview));
        Assert.Equal(new DuplicateDecisionDto(DuplicateMatchDecision.Confirmed, now, "owner"), ownersGroup.Decision);
        // Who decided is the owner's sign-in name: everyone else reads the decision and when, never the name.
        Assert.Equal(new DuplicateDecisionDto(DuplicateMatchDecision.Confirmed, now, null), members.Items.Single(group => group.Key == key).Decision);
        Assert.Null(owners.Items.Single(group => group.Key != key).Decision);
        // ?decision=Confirmed lists the confirmed ones alone.
        Assert.Equal((1, key), (confirmed.TotalCount, Assert.Single(confirmed.Items).Key));
    }

    [Fact]
    public async Task Decide_dismissed_takes_the_group_off_every_list_but_the_dismissed_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        await PlantPossibleMatchAsync("Brazil", 1985);
        await PlantCopiesAsync("Alien", 1979, AlienImdbId);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);

        //Act
        var response = await DecideAsync(key, "Dismissed", _owner.AccessToken);
        var all = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _member.AccessToken), cancellationToken);
        var possible = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?class=Possible", _member.AccessToken), cancellationToken);
        var dismissed = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?decision=dismissed", _member.AccessToken), cancellationToken);
        var dismissedPossible = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?class=Possible&decision=Dismissed", _member.AccessToken), cancellationToken);
        var confirmed = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?decision=Confirmed", _member.AccessToken), cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // Vasyl's decision 2 — gone from every tab, the totals counting without it.
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(["Alien", "Brazil"], all.Items.Select(TitleOf));
        Assert.Equal(1, possible.TotalCount);
        Assert.Equal(["Brazil"], possible.Items.Select(TitleOf));
        // ... and listed apart, in any letter case, where it can be undone.
        foreach (var list in new[] { dismissed, dismissedPossible })
        {
            Assert.Equal(1, list.TotalCount);
            var group = Assert.Single(list.Items);
            Assert.Equal((key, (DuplicateMatchDecision?)DuplicateMatchDecision.Dismissed), (group.Key, group.Decision?.Decision));
        }

        Assert.Equal(0, confirmed.TotalCount);
        Assert.Empty(confirmed.Items);
    }

    [Theory]
    [InlineData("confirmed", DuplicateMatchDecision.Confirmed)]
    [InlineData("DISMISSED", DuplicateMatchDecision.Dismissed)]
    [InlineData("  Confirmed  ", DuplicateMatchDecision.Confirmed)]
    public async Task Decide_takes_a_decision_in_any_letter_case(string decision, DuplicateMatchDecision stored)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);

        //Act
        var response = await DecideAsync(key, decision, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(stored, (await SingleDecisionAsync(cancellationToken)).Decision);
    }

    [Fact]
    public async Task Decide_the_same_decision_again_is_204_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        var decidedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await DecideAsync(key, "Confirmed", _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));

        //Act
        var response = await DecideAsync(key, "confirmed", _owner.AccessToken);

        //Assert
        // A double click or a retry: no row change, no second audit row.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = await SingleDecisionAsync(cancellationToken);
        Assert.Equal((DuplicateMatchDecision.Confirmed, decidedUtc), (row.Decision, row.DecidedUtc));
        Assert.Equal([AuditAction.DuplicateMatchConfirmed], (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Theory]
    [InlineData("Confirmed", "Dismissed", AuditAction.DuplicateMatchConfirmed, AuditAction.DuplicateMatchDismissed)]
    [InlineData("Dismissed", "Confirmed", AuditAction.DuplicateMatchDismissed, AuditAction.DuplicateMatchConfirmed)]
    public async Task Decide_another_decision_changes_the_row_and_writes_a_second_audit_row(
        string first, string second, AuditAction firstAction, AuditAction secondAction)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        var createdUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await DecideAsync(key, first, _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var changedUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await DecideAsync(key, second, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // One row per group, changed in place: when it was first decided stays, when it was last decided moves.
        var row = await SingleDecisionAsync(cancellationToken);
        Assert.Equal((Enum.Parse<DuplicateMatchDecision>(second), changedUtc, createdUtc), (row.Decision, row.DecidedUtc, row.CreatedUtc));
        // A change of mind records the decision it changed to, on the same target.
        var audit = await AuditAsync(cancellationToken);
        Assert.Equal([firstAction, secondAction], audit.Select(auditRow => auditRow.Action));
        Assert.All(audit, auditRow => Assert.Equal(Guid.ParseExact(key, "N"), auditRow.TargetId));
    }

    [Theory]
    [InlineData(DuplicateMatchDecision.Confirmed)]
    [InlineData(DuplicateMatchDecision.Dismissed)]
    public async Task Decide_when_another_request_decides_first_is_204_with_one_row_and_the_last_decision(DuplicateMatchDecision winner)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        // Deterministic race: both found no row, and another request's decision lands just before this one's — through
        // a context of its own, as a parallel request would.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DuplicateMatchDecisionEntity>().Any(entry => entry.State == EntityState.Added),
            async _ =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                await new DuplicateMatchDecisionDataFactory(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), _factory.Time)
                    .Create(key, winner, _owner.User.Id);
            });

        //Act
        var response = await DecideAsync(key, "Dismissed", _owner.AccessToken);

        //Assert
        // The unique index lets one commit, and the loser is given just after it: a 204, not a 500; one row saying the
        // last decision, and an audit row only when it changed what the winner said.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(DuplicateMatchDecision.Dismissed, (await SingleDecisionAsync(cancellationToken)).Decision);
        AuditAction[] recorded = winner == DuplicateMatchDecision.Dismissed ? [] : [AuditAction.DuplicateMatchDismissed];
        Assert.Equal(recorded, (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task Decide_as_a_member_is_403_with_the_owner_sentence_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);

        //Act
        var response = await DecideAsync(key, "Confirmed", _member.AccessToken);

        //Assert
        // Vasyl's decision 1 — the owner's alone; members read it.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Decide_and_undo_without_a_token_are_401_and_write_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        await DecideAsync(key, "Dismissed", _owner.AccessToken);
        var before = await AuditAsync(cancellationToken);

        //Act
        var decide = await DecideAsync(key, "Confirmed", token: null);
        var undo = await _factory.DeleteHttpRequest(DecisionUrl(key));

        //Assert
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (decide.StatusCode, undo.StatusCode));
        Assert.Equal(SignInRequiredBody, await decide.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(SignInRequiredBody, await undo.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(DuplicateMatchDecision.Dismissed, (await SingleDecisionAsync(cancellationToken)).Decision);
        Assert.Equal(before.Count, (await AuditAsync(cancellationToken)).Count);
    }

    [Theory]
    [InlineData(UnknownKey)]
    // Of another shape: no group can have it, so the library isn't read for it, and it isn't told apart either.
    [InlineData("sample")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef")]
    public async Task Decide_and_undo_of_a_key_no_group_has_are_bare_404s(string key)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);

        //Act
        var decide = await DecideAsync(key, "Confirmed", _owner.AccessToken);
        var undo = await _factory.DeleteHttpRequest(DecisionUrl(key), _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (decide.StatusCode, undo.StatusCode));
        Assert.Empty(await decide.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(await undo.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Theory]
    [MemberData(nameof(DecisionsThatArentOne))]
    public async Task Decide_with_a_decision_missing_or_not_one_is_400_with_the_sentence_and_writes_nothing(string json, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);

        //Act
        var response = await PutJsonTextAsync(key, json);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Decide_checks_the_decision_before_the_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await PutJsonTextAsync(UnknownKey, """{"decision":"Maybe"}""");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotADecision("Maybe"), await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("{not json")]
    // A decision is a name, never a number.
    [InlineData("""{"decision":1}""")]
    [InlineData("""{"decision":true}""")]
    [InlineData("""["Confirmed"]""")]
    [InlineData("\"Confirmed\"")]
    public async Task Decide_with_a_body_that_isnt_a_decision_is_400_with_its_sentence_never_a_validation_body(string json)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);

        //Act
        var response = await PutJsonTextAsync(key, json);

        //Assert
        // The { error } shape the Duplicates page shows: never [ApiController]'s problem details.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(UnreadableBodyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Undo_forgets_the_decision_with_an_audit_row_and_the_group_is_back_undecided()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        await DecideAsync(key, "Dismissed", _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));
        var undoneUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.DeleteHttpRequest(DecisionUrl(key), _owner.AccessToken);
        var all = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _member.AccessToken), cancellationToken);
        var dismissed = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?decision=Dismissed", _member.AccessToken), cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.DuplicateMatchDecisions.CountAsync(cancellationToken)));
        var audit = await AuditAsync(cancellationToken);
        Assert.Equal([AuditAction.DuplicateMatchDismissed, AuditAction.DuplicateMatchDecisionUndone], audit.Select(row => row.Action));
        var undone = audit[1];
        Assert.Equal(
            ((Guid?)_owner.User.Id, "owner", (AuditTargetKind?)AuditTargetKind.DuplicateMatch, (Guid?)Guid.ParseExact(key, "N"), "Arrival (2016)", undoneUtc),
            (undone.ActorUserId, undone.ActorUserName, undone.TargetKind, undone.TargetId, undone.TargetName, undone.CreatedUtc));
        // Back on the list, asked about again, and off the dismissed one.
        var group = Assert.Single(all.Items);
        Assert.Equal((key, true, (DuplicateDecisionDto?)null), (group.Key, group.NeedsReview, group.Decision));
        Assert.Equal(0, dismissed.TotalCount);
    }

    [Fact]
    public async Task Undo_without_a_decision_is_204_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);

        //Act
        var response = await _factory.DeleteHttpRequest(DecisionUrl(key), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Undo_as_a_member_is_403_with_the_owner_sentence_and_keeps_the_decision()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        await DecideAsync(key, "Dismissed", _owner.AccessToken);

        //Act
        var response = await _factory.DeleteHttpRequest(DecisionUrl(key), _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(DuplicateMatchDecision.Dismissed, (await SingleDecisionAsync(cancellationToken)).Decision);
        Assert.Equal([AuditAction.DuplicateMatchDismissed], (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task A_decision_does_not_follow_a_group_whose_members_changed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var confirmedKey = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        await DecideAsync(confirmedKey, "Confirmed", _owner.AccessToken);
        // A third library reports a third Arrival of 2016: the owner confirmed two works, not these three.
        var attic = await _integrations.Create("jellyfin", "Attic", AtticBaseUrl, apiKey: ApiKey);
        await PlantMovieAsync(attic, "Arrival", 2016, "/media/attic/Arrival (2016).mkv");

        //Act
        var list = await ReadListAsync(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken), cancellationToken);
        var confirmed = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?decision=Confirmed", _owner.AccessToken), cancellationToken);
        var decideOld = await DecideAsync(confirmedKey, "Dismissed", _owner.AccessToken);
        var undoOld = await _factory.DeleteHttpRequest(DecisionUrl(confirmedKey), _owner.AccessToken);

        //Assert
        // ADR 0029 — the group of three has a key of its own and is asked about again.
        var group = Assert.Single(list.Items);
        Assert.NotEqual(confirmedKey, group.Key);
        Assert.Equal((3, true, (DuplicateDecisionDto?)null), (group.Files.Count, group.NeedsReview, group.Decision));
        Assert.Equal(0, confirmed.TotalCount);
        // The old key names no group now: nothing to decide or undo by it.
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (decideOld.StatusCode, undoOld.StatusCode));
        // Its row stays, matching nothing, and comes back into force if the same two works do.
        var row = await SingleDecisionAsync(cancellationToken);
        Assert.Equal((confirmedKey, DuplicateMatchDecision.Confirmed), (row.GroupKey, row.Decision));
        Assert.Equal([AuditAction.DuplicateMatchConfirmed], (await AuditAsync(cancellationToken)).Select(audit => audit.Action));
    }

    [Fact]
    public async Task A_decision_changes_nothing_in_the_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantPossibleMatchAsync("Arrival", 2016);
        var key = await KeyOfAsync(DuplicateClass.Possible, "Arrival", cancellationToken);
        var before = await LibraryCountsAsync(cancellationToken);

        //Act
        var confirm = await DecideAsync(key, "Confirmed", _owner.AccessToken);
        var confirmed = await LibraryCountsAsync(cancellationToken);
        var dismiss = await DecideAsync(key, "Dismissed", _owner.AccessToken);
        var dismissed = await LibraryCountsAsync(cancellationToken);
        var undo = await _factory.DeleteHttpRequest(DecisionUrl(key), _owner.AccessToken);
        var undone = await LibraryCountsAsync(cancellationToken);

        //Assert
        // A note on the list: confirming makes no works one and links nothing, dismissing removes nothing.
        Assert.Equal(
            (HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent),
            (confirm.StatusCode, dismiss.StatusCode, undo.StatusCode));
        Assert.Equal((2, 2, 2, 0), before);
        Assert.All(new[] { confirmed, dismissed, undone }, counts => Assert.Equal(before, counts));
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string NotADecision(string echo) => $"“{echo}” is not a decision. Use Confirmed or Dismissed.";

    private static string DecisionUrl(string key) => $"{BaseUrl}/{Uri.EscapeDataString(key)}/decision";

    private static string MoviePath(string title, int year) => $"/media/movies/{title} ({year})/{title} ({year}).mkv";

    private Task<HttpResponseMessage> DecideAsync(string key, string decision, string? token) =>
        _factory.PutHttpRequest(DecisionUrl(key), token, new DuplicateDecisionRequest(decision));

    /// <summary>The owner's decision with <paramref name="json"/> as the body as it is — JSON the helpers can't write, such
    /// as a field left out or a malformed body; no body at all when it is empty.</summary>
    private Task<HttpResponseMessage> PutJsonTextAsync(string key, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, DecisionUrl(key));
        if (json.Length > 0)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _owner.AccessToken);
        return _factory.SendHttpRequest(request);
    }

    // A movie a library reported in an item of its own — its title and year, the IMDb id given, one version and one
    // name — what a sync stores.
    private async Task PlantMovieAsync(IntegrationConnectionEntity library, string title, int year, string servicePath, string? imdbId = null)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var work = await _works.Create();
        await _works.AddTitle(work.Id, title, MediaTitleKind.Display, year, library.Id, library.Name, itemId);
        if (imdbId is not null)
            await _works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, library.Id, library.Name, itemId);
        var version = await _versions.Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await _physicalFiles.Create(version.Id, servicePath, sourceConnectionId: library.Id, sourceName: library.Name);
    }

    // A Possible group: the movie in each library with one title and year and no id at all.
    private async Task PlantPossibleMatchAsync(string title, int year)
    {
        await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year));
        await PlantMovieAsync(_bedroom, title, year, $"/media/films/{title} ({year}).mkv");
    }

    // A SameWork group: the movie in each library under one IMDb id.
    private async Task PlantCopiesAsync(string title, int year, string imdbId)
    {
        await PlantMovieAsync(_livingRoom, title, year, MoviePath(title, year), imdbId);
        await PlantMovieAsync(_bedroom, title, year, $"/media/films/{title} ({year}).mkv", imdbId);
    }

    /// <summary>The key the list gives the group of <paramref name="duplicateClass"/> whose works are called
    /// <paramref name="title"/>.</summary>
    private async Task<string> KeyOfAsync(DuplicateClass duplicateClass, string title, CancellationToken cancellationToken)
    {
        var list = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?class={duplicateClass}", _owner.AccessToken), cancellationToken);
        return list.Items.Single(group => TitleOf(group) == title).Key;
    }

    // The title a group is ordered by, as its first file's first work shows it: every planted group's works share one.
    private static string? TitleOf(LibraryDuplicateGroupDto group) => group.Files[0].Works[0].Title;

    private static async Task<LibraryDuplicateListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private async Task<DuplicateMatchDecisionEntity> SingleDecisionAsync(CancellationToken cancellationToken)
    {
        DuplicateMatchDecisionEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DuplicateMatchDecisions.AsNoTracking().SingleAsync(cancellationToken));
        return row!;
    }

    /// <summary>The audit rows in the order they were recorded.</summary>
    private async Task<List<AuditEventEntity>> AuditAsync(CancellationToken cancellationToken)
    {
        var rows = new List<AuditEventEntity>();
        await _factory.WithDbAsync(async db => rows.AddRange(
            (await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(auditEvent => auditEvent.CreatedUtc)));
        return rows;
    }

    // What a decision must never touch: the works, their versions and files, and the links that would make works one.
    private async Task<(int Works, int Versions, int Files, int Links)> LibraryCountsAsync(CancellationToken cancellationToken)
    {
        var counts = (0, 0, 0, 0);
        await _factory.WithDbAsync(async db => counts = (
            await db.MediaWorks.CountAsync(cancellationToken),
            await db.MediaVersions.CountAsync(cancellationToken),
            await db.PhysicalFiles.CountAsync(cancellationToken),
            await db.LibraryItemLinks.CountAsync(cancellationToken)));
        return counts;
    }

    private async Task AssertNothingWrittenAsync(CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DuplicateMatchDecisions.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
}
