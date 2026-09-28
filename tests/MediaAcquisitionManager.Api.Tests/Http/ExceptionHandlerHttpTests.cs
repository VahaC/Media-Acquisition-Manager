using System.Net;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Http.Probes;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Http;

public sealed class ExceptionHandlerHttpTests : IClassFixture<PipelineProbeApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = PipelineProbeController.BaseUrl + "/throw";
    private const string LimitedBodyUrl = PipelineProbeController.BaseUrl + "/limited-body";

    // A W3C traceparent, so the test knows which trace id the answer must quote.
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string TraceParent = "00-" + TraceId + "-00f067aa0ba902b7-01";

    private readonly PipelineProbeApiFactory _factory;

    public ExceptionHandlerHttpTests(PipelineProbeApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Unhandled_exception_is_500_json_error_quoting_the_trace_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        request.Headers.Add("traceparent", TraceParent);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            $"The server failed to process this request. Quote trace id {TraceId} when you report it.",
            await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Unhandled_exception_is_logged_as_an_error_with_the_trace_id_and_the_exception()
    {
        //Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        request.Headers.Add("traceparent", TraceParent);

        //Act
        await _factory.SendHttpRequest(request);

        //Assert
        // The answer tells the user to quote the trace id; this is the line it has to match.
        var entry = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(GlobalExceptionHandler).FullName);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(TraceId, entry.Message, StringComparison.Ordinal);
        var exception = Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal(PipelineProbeController.ThrownMessage, exception.Message);
    }

    [Fact]
    public async Task Unhandled_exception_never_puts_the_exception_text_in_the_body()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(PipelineProbeController.ThrownMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("probe", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhandled_exception_answer_still_carries_the_security_headers()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
    }

    [Fact]
    public async Task Body_over_the_size_limit_is_413_json_error_not_500()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var oversized = new string('x', PipelineProbeController.BodyLimitBytes * 4);

        //Act
        var response = await _factory.PostHttpRequest(LimitedBodyUrl, body: oversized);

        //Assert
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("The request body is larger than this server accepts.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Body_over_the_size_limit_is_logged_as_information_not_as_an_error()
    {
        //Arrange
        var oversized = new string('x', PipelineProbeController.BodyLimitBytes * 4);

        //Act
        await _factory.PostHttpRequest(LimitedBodyUrl, body: oversized);

        //Assert
        // A client's fault must not ship off the box as an Error; it still leaves a trace.
        var entry = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(GlobalExceptionHandler).FullName);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("413", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.Logs.Entries, logged => logged.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Body_within_the_size_limit_is_accepted()
    {
        //Arrange
        var small = new string('x', PipelineProbeController.BodyLimitBytes / 2);

        //Act
        var response = await _factory.PostHttpRequest(LimitedBodyUrl, body: small);

        //Assert
        // Guards the guard: the probe itself accepts a body that fits, so the 413 above is the limit.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
