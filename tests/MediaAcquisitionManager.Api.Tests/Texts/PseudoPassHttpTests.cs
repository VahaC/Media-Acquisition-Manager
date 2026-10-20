using System.Net;
using System.Text.Json;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the harness speaks the pass's language: in the pseudo pass every request of the factory asks for
// the pseudo-language and the host answers in it, while the readers give the tests back the English they assert; in the
// normal pass nothing changes. A request that names its own language is answered in it in both passes.
public sealed class PseudoPassHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string UnknownApiUrl = "/api/nope";
    private const string English = "No API endpoint matches GET /api/nope.";

    private readonly MediaAcquisitionManagerApiFactory _factory;

    public PseudoPassHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_request_of_the_factory_is_answered_in_the_passes_language()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(UnknownApiUrl);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(TestLanguage.IsPseudo, AcceptLanguageHandler.AskedForPseudo(response.RequestMessage));
        Assert.Equal(TestLanguage.IsPseudo ? $"⟦{English}⟧" : English, await ReadRawErrorAsync(response, cancellationToken));
    }

    [Fact]
    public async Task ReadErrorAsync_gives_the_english_in_both_passes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Get, UnknownApiUrl);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(English, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task A_request_that_names_its_language_is_answered_in_it_in_both_passes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(UnknownApiUrl, language: "en-GB");

        //Assert
        Assert.False(AcceptLanguageHandler.AskedForPseudo(response.RequestMessage));
        Assert.Equal(English, await ReadRawErrorAsync(response, cancellationToken));
        Assert.Equal(English, await response.ReadErrorAsync(cancellationToken));
    }

    private static async Task<string?> ReadRawErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("error").GetString();
    }
}
