using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-47 WP14 (ADR 0042, design §8.6) — a replacement's page in Ukrainian, as the owner reads it: its stored reason is
// rendered in the reader's language when it is read — the new files counted in the noun's own form with their paths in
// «…» joined by «і», a work with no title named by its label where a title would stand, a reason an older build stored
// in English converted at start —, the Download dialog words the owner's confirmation as its claim, and a refused Delete
// for good says what it needs. The same page without the header answers English. Each answer spelled here, never read
// from the locale files.
public sealed class ReplacementsUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string OptionsUrl = "/api/replacements/options";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private ReplacementQuarantinePlanter _planter = null!;

    public ReplacementsUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _planter = new ReplacementQuarantinePlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Get_of_a_cancelled_replacement_asked_in_ukrainian_reads_its_reason_in_ukrainian()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.Cancelled, [], reason: Message.Of("replacements.keep-both.reason"));

        //Act
        var ukrainian = await GetAsync(planted.Replacement.Id, "uk", cancellationToken);
        var english = await GetAsync(planted.Replacement.Id, null, cancellationToken);

        //Assert
        Assert.Equal("Ви скасували заміну, тож нічого не буде замінено.", ukrainian.Reason);
        Assert.Equal("You cancelled the replacement, so nothing will be replaced.", english.Reason);
    }

    [Theory]
    [InlineData(2, "2 нові файли ідентифіковано як фільм Alien (1979): «/media/movies/1.mkv» і «/media/movies/2.mkv»; застосунок не може визначити, який із них замінює стару копію.")]
    [InlineData(5, "5 нових файлів ідентифіковано як фільм Alien (1979): «/media/movies/1.mkv», «/media/movies/2.mkv», «/media/movies/3.mkv», «/media/movies/4.mkv» і «/media/movies/5.mkv»; застосунок не може визначити, який із них замінює стару копію.")]
    public async Task Get_asked_in_ukrainian_counts_the_new_files_of_a_work_in_their_own_form(int count, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var paths = Enumerable.Range(1, count).Select(n => (MessageValue)new TextValue($"/media/movies/{n}.mkv"));
        var reason = Message.Of("replacements.pairing.new-files-film", ("count", count), ("work", "Alien (1979)"), ("paths", new ListValue(paths)));
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], reason: reason);

        //Act
        var details = await GetAsync(planted.Replacement.Id, "uk", cancellationToken);

        //Assert
        Assert.Equal(expected, details.Reason);
    }

    [Fact]
    public async Task Get_asked_in_ukrainian_names_a_work_with_no_title_by_its_label_where_a_title_would_stand()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var reason = Message.Of("replacements.pairing.no-new-file-film", ("work", ReplacementPairingWork.NameOf(null, 2005)), ("path", "/media/movies/Untitled (2005).mkv"));
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], reason: reason);

        //Act
        var ukrainian = await GetAsync(planted.Replacement.Id, "uk", cancellationToken);
        var english = await GetAsync(planted.Replacement.Id, null, cancellationToken);

        //Assert
        Assert.Equal(
            "Жодного нового файлу не ідентифіковано як фільм без назви (2005), тож копію «/media/movies/Untitled (2005).mkv» нічим замінити.",
            ukrainian.Reason);
        Assert.Equal("No new file is a work with no title (2005), so “/media/movies/Untitled (2005).mkv” has nothing to replace it.", english.Reason);
    }

    [Fact]
    public async Task Get_asked_in_ukrainian_reads_a_reason_an_older_build_stored_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], reason: Message.Of("replacements.keep-both.reason"));
        await _factory.ConvertStoredEnglishAsync(
            "ReplacementJobs",
            "Reason",
            planted.Replacement.Id,
            "Two new files are Alien (1979): “/media/movies/A.mkv” and “/media/movies/B.mkv”; the app can't tell which one replaces the old copy.");

        //Act
        var details = await GetAsync(planted.Replacement.Id, "uk", cancellationToken);

        //Assert
        Assert.Equal(
            "2 нові файли ідентифіковано як фільм Alien (1979): «/media/movies/A.mkv» і «/media/movies/B.mkv»; застосунок не може визначити, який із них замінює стару копію.",
            details.Reason);
    }

    [Fact]
    public async Task Get_asked_in_ukrainian_names_an_older_builds_work_with_no_title_by_its_ukrainian_label()
    {
        //Arrange
        // MAM-47 WP14 review — the label's English words an older build stored where a name goes read back as the label,
        // so Ukrainian writes «без назви», never "a work with no title".
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.NeedsReview, [], reason: Message.Of("replacements.keep-both.reason"));
        await _factory.ConvertStoredEnglishAsync(
            "ReplacementJobs",
            "Reason",
            planted.Replacement.Id,
            "No new file is a work with no title (2005), so “/media/movies/Untitled (2005).mkv” has nothing to replace it.");

        //Act
        var details = await GetAsync(planted.Replacement.Id, "uk", cancellationToken);

        //Assert
        Assert.Equal(
            "Жодного нового файлу не ідентифіковано як фільм без назви (2005), тож копію «/media/movies/Untitled (2005).mkv» нічим замінити.",
            details.Reason);
    }

    [Fact]
    public async Task Options_asked_in_ukrainian_word_the_owners_confirmation_as_the_claim_in_ukrainian()
    {
        //Arrange
        // MAM-47 WP14 review (Vasyl's Q1) — the tick's words are the claim, written in the reader's language: a release
        // Toloka lists with no id matches Alien by title and year alone, so the owner confirms what it is.
        var cancellationToken = TestContext.Current.CancellationToken;
        await _harness.PlantFilmAsync("Alien", 1979, "/media/movies/Alien (1979)/Alien (1979).mkv", inode: 1, imdbId: "tt0078748");
        await _harness.InspectAsync(cancellationToken);
        var release = await _harness.FindAsync("Alien", 1979, "Alien (1979) BDRip 1080p");

        //Act
        var ukrainian = await OptionsAsync(release.Id, "uk", cancellationToken);
        var english = await OptionsAsync(release.Id, null, cancellationToken);

        //Assert
        var work = Assert.Single(ukrainian.Works);
        Assert.Equal((true, "Цей реліз — Alien (1979)"), (work.NeedsConfirmation, work.ConfirmationText));
        Assert.Equal("This release is Alien (1979)", Assert.Single(english.Works).ConfirmationText);
    }

    [Fact]
    public async Task DeleteForGood_without_the_confirmed_names_asked_in_ukrainian_is_400_saying_what_it_needs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await _planter.PlantAsync(ReplacementJobState.Quarantined, []);

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/{planted.Replacement.Id}/delete-for-good", _harness.Owner.AccessToken, new DeleteQuarantinedRequest([]), language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Щоб остаточно видалити старі копії, потрібні файли, які ви підтвердили.","code":"replacements.delete-for-good.names-missing"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task<ReplacementDetailsDto> GetAsync(Guid replacementJobId, string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{replacementJobId}", _harness.Owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An answer asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return (await response.Content.ReadFromJsonAsync<ReplacementDetailsDto>(options, cancellationToken))!;
    }

    private async Task<ReplacementOptionsResponse> OptionsAsync(Guid releaseId, string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(
            $"{OptionsUrl}?releaseId={releaseId}&destinationId={_harness.Movies.Id}", _harness.Owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An answer asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(options, cancellationToken))!;
    }
}
