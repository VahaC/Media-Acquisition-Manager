using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Tests.Health;

public sealed class HealthHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/health";
    private const string ReadyUrl = BaseUrl + "/ready";

    private readonly MediaAcquisitionManagerApiFactory _factory;

    public HealthHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Liveness_without_a_token_is_200_healthy_with_no_checks()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(HealthStatus.Healthy, body!.Status);
        Assert.Empty(body.Checks);
    }

    [Fact]
    public async Task Readiness_without_a_token_is_200_with_the_database_check_healthy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(ReadyUrl);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(HealthStatus.Healthy, body!.Status);
        var check = Assert.Single(body.Checks);
        Assert.Equal(new HealthCheckDto(
            "database", HealthStatus.Healthy, "The database is reachable and every migration is applied."), check);
    }

    [Theory]
    [InlineData(BaseUrl)]
    [InlineData(ReadyUrl)]
    public async Task Health_endpoints_answer_camel_case_json_with_statuses_as_names(string url)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(url);

        //Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("checks").ValueKind);
    }
}
