using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042, R22, design §6.1, §6.8) — the upgrade from a build that stored sentences in English, over the real start
// (StoredEnglishApiFactory plants the older rows before the host opens its database): the startup converter runs after
// the migrations and before the host answers anything, so the first request already reads key and values — the Jobs page
// in the reader's language, and the links' repair, whose query selects by the restart sentence's key, leaves the link an
// older build marked as restarted to the owner's own Retry; a column whose read fails at start is logged and the boot goes
// on. No ResetAsync: the rows planted before the start are the subject, and every test here only reads, so their order
// doesn't matter.
public sealed class StoredTextConverterStartupHttpTests : IClassFixture<StoredEnglishApiFactory>, IAsyncLifetime
{
    private const string DownloadsUrl = "/api/downloads";
    private const string CatchUpUrl = "/api/library/tmdb/catch-up";

    private readonly StoredEnglishApiFactory _factory;
    private AuthorizedUser _owner = null!;

    public StoredTextConverterStartupHttpTests(StoredEnglishApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => _owner = await new UserSeeder(_factory.Scope).CreateSession(_factory.Owner);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Start_converts_the_older_builds_english_before_the_host_answers_a_request()
    {
        //Arrange

        //Act
        var stored = _factory.StoredAtStart;

        //Assert
        Assert.Equal(
            [
                MessageJson.Write(StoredEnglishApiFactory.FailedReason),
                MessageJson.Write(StoredEnglishApiFactory.MissingReason),
                MessageJson.Write(StoredEnglishApiFactory.RestartedReason),
                MessageJson.Write(StoredEnglishApiFactory.SwitchedOffReason),
            ],
            stored);
        Assert.Equal(
            [
                "Converted 1 stored texts of DownloadJobs.BlockReason; 0 with a cut value, 0 shortened to fit, 0 kept as their words.",
                "Converted 1 stored texts of DownloadJobs.Error; 0 with a cut value, 0 shortened to fit, 0 kept as their words.",
                "Converted 2 stored texts of LibraryItemLinks.WriteError; 0 with a cut value, 0 shortened to fit, 0 kept as their words.",
            ],
            _factory.Logs.Entries
                .Where(entry => entry.Category == typeof(StoredTextConverter).FullName && entry.Level == LogLevel.Information)
                .Select(entry => entry.Message));
    }

    [Fact]
    public async Task Start_with_a_column_whose_read_fails_logs_it_converts_the_others_and_serves()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(DownloadsUrl, _owner.AccessToken);

        //Assert
        // The host started and answers; the failed column is named, never a text; the columns after it were converted.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(StoredTextConverter).FullName && entry.Level == LogLevel.Error);
        Assert.Equal(
            ($"The conversion of the stored texts of {StoredEnglishApiFactory.FailingColumn} failed; they are shown as stored until the next start.", StoredEnglishApiFactory.FailingReadError),
            (error.Message, error.Exception?.Message));
        Assert.Equal(MessageJson.Write(StoredEnglishApiFactory.FailedReason), _factory.StoredAtStart[0]);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.PlainJsonOptions, cancellationToken));
    }

    [Fact]
    public async Task Jobs_list_in_ukrainian_reads_the_reasons_an_older_build_stored_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(DownloadsUrl, _owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.PlainJsonOptions, cancellationToken);
        (Guid Id, string? Error, string? BlockReason)[] expected =
        [
            (_factory.FailedJobId, TestTexts.Uk(StoredEnglishApiFactory.FailedReason), null),
            (_factory.MissingJobId, null, TestTexts.Uk(StoredEnglishApiFactory.MissingReason)),
        ];
        Assert.Equal(expected, list!.Items.Select(job => (job.Id, job.Error, job.BlockReason)).OrderBy(job => job.Id == _factory.FailedJobId ? 0 : 1));
    }

    [Fact]
    public async Task Catch_up_leaves_out_the_link_an_older_build_marked_as_restarted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        // Only the switched-off one: the restarted link waits for the owner's Retry, as Jellyfin may still be on it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new LibraryLinkCatchUpResponse(1, null),
            await response.Content.ReadFromJsonAsync<LibraryLinkCatchUpResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken));
    }
}
