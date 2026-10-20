using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the one writer of an error body outside MVC (the 401 challenge, the token's 429 and 403, the
// exception handler and the rate limiter take it in WP5): the status, and { error, code } in the request's language with
// the message's key as the code, in the bytes MVC writes. Over a DefaultHttpContext, since no endpoint uses it yet; the
// controllers' path is ErrorBodyHttpTests' and SpaFallbackHttpTests' over real HTTP.
public sealed class ApiErrorBodyTests
{
    private static readonly Message UnknownEndpoint =
        Message.Of("common.request.unknown-endpoint", ("method", "GET"), ("path", "/api/nope"));

    [Theory]
    [InlineData(null, """{"error":"No API endpoint matches GET /api/nope.","code":"common.request.unknown-endpoint"}""")]
    [InlineData("qps-ploc", """{"error":"⟦No API endpoint matches GET /api/nope.⟧","code":"common.request.unknown-endpoint"}""")]
    public async Task WriteAsync_writes_the_status_and_the_body_in_the_requests_language(string? acceptLanguage, string expected)
    {
        //Arrange
        var context = Context(acceptLanguage);

        //Act
        await ApiErrorBody.WriteAsync(context, StatusCodes.Status404NotFound, UnknownEndpoint);

        //Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.Ordinal);
        context.Response.Body.Position = 0;
        Assert.Equal(expected, await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void For_takes_the_messages_key_as_the_code()
    {
        //Arrange
        var context = Context(acceptLanguage: null);

        //Act
        var body = ApiErrorBody.For(context, UnknownEndpoint);

        //Assert
        Assert.Equal(("No API endpoint matches GET /api/nope.", "common.request.unknown-endpoint"), (body.Error, body.Code));
    }

    // The request's services as the host registers them for rendering (AddServerTexts), the pseudo-language accepted.
    private static DefaultHttpContext Context(string? acceptLanguage)
    {
        var context = new DefaultHttpContext();
        if (acceptLanguage is not null)
            context.Request.Headers.AcceptLanguage = acceptLanguage;
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context })
            .AddSingleton(Options.Create(new TextOptions { Strict = true, Pseudo = true }))
            .AddSingleton<ILoggerFactory>(new LoggerFactory([new RecordingLoggerProvider()]))
            .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
            .AddSingleton<IRequestTexts, RequestTexts>()
            .BuildServiceProvider();
        return context;
    }
}
