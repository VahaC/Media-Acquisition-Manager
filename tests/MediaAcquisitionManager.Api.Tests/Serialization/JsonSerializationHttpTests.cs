using System.Text.Json;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Http.Probes;

namespace MediaAcquisitionManager.Api.Tests.Serialization;

// Read as raw JSON on purpose: deserializing with the test's own options would hide exactly the
// wire-format differences these tests exist to catch.
public sealed class JsonSerializationHttpTests : IClassFixture<PipelineProbeApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = PipelineProbeController.BaseUrl + "/instant";

    private readonly PipelineProbeApiFactory _factory;

    public JsonSerializationHttpTests(PipelineProbeApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task DateTime_without_a_kind_is_written_as_utc_with_a_trailing_Z()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("2026-03-29T01:30:15Z", document.RootElement.GetProperty("instantUtc").GetString());
    }

    [Fact]
    public async Task Enum_is_written_as_its_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(nameof(PipelineProbeKind.Second), document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Property_names_are_camel_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            ["instantUtc", "kind"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }
}
