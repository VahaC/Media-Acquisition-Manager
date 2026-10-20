using System.Net;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the harness's exact error bodies are the API's bytes: the tests that compare a whole answer (the
// anti-probing ones prove two causes answer the same bytes) compare against ErrorBody.Json, so it must escape what the
// API escapes and nothing else, and wrap the sentence exactly when the pass does.
public sealed class ErrorBodyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private readonly MediaAcquisitionManagerApiFactory _factory;

    public ErrorBodyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Json_is_the_bytes_the_api_writes_for_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest("/api/nope/deeper");

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            ErrorBody.Json("No API endpoint matches POST /api/nope/deeper.", "common.request.unknown-endpoint"),
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Json_escapes_what_the_api_escapes_and_nothing_else()
    {
        //Arrange
        // ' + & stay as they are in a path; the default JSON encoder would write each as \uXXXX, the API's doesn't.
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest("/api/it's+a&b");

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            ErrorBody.Json("No API endpoint matches GET /api/it's+a&b.", "common.request.unknown-endpoint"),
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task ReadErrorBodyAsync_reads_the_sentence_and_its_code()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.DeleteHttpRequest("/api/nope");

        //Assert
        Assert.Equal(("No API endpoint matches DELETE /api/nope.", "common.request.unknown-endpoint"), await response.ReadErrorBodyAsync(cancellationToken));
    }

    [Fact]
    public async Task ReadErrorCodeAsync_reads_the_code_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PutHttpRequest("/api/nope");

        //Assert
        Assert.Equal("common.request.unknown-endpoint", await response.ReadErrorCodeAsync(cancellationToken));
    }
}
