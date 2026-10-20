using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the facade the mappers, error writers and MCP tools render through: the current request's
// language, English outside a request (the pseudo-language in the test suite's second pass, so a background render shows
// up there), English for logs, and one Warning per key and language when production had to fall back.
public sealed class RequestTextsTests
{
    private static readonly Message UnknownEndpoint =
        Message.Of("common.request.unknown-endpoint", ("method", "GET"), ("path", "/api/nope"));

    [Theory]
    [InlineData(false, TextLanguage.English)]
    [InlineData(true, TextLanguage.Pseudo)]
    public void Language_outside_a_request_is_english_or_the_pseudo_language_when_it_is_on(bool pseudo, TextLanguage expected)
    {
        //Arrange
        var texts = Create(httpContext: null, new TextOptions { Pseudo = pseudo });

        //Act
        var language = texts.Language;

        //Assert
        Assert.Equal(expected, language);
    }

    [Theory]
    [InlineData("uk-UA", false, TextLanguage.Ukrainian)]
    [InlineData("qps-ploc", false, TextLanguage.English)]
    [InlineData("qps-ploc", true, TextLanguage.Pseudo)]
    public void Language_in_a_request_is_what_its_accept_language_asks_for(string header, bool pseudo, TextLanguage expected)
    {
        //Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = header;
        var texts = Create(context, new TextOptions { Pseudo = pseudo });

        //Act
        var language = texts.Language;

        //Assert
        Assert.Equal(expected, language);
    }

    [Fact]
    public void Render_writes_in_the_requests_language_and_English_never_wraps()
    {
        //Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "qps-ploc";
        var texts = Create(context, new TextOptions { Pseudo = true, Strict = true });

        //Act
        var rendered = texts.Render(UnknownEndpoint);
        var english = texts.English(UnknownEndpoint);

        //Assert
        Assert.Equal("⟦No API endpoint matches GET /api/nope.⟧", rendered);
        Assert.Equal("No API endpoint matches GET /api/nope.", english);
        Assert.Null(texts.Render(null));
        Assert.Null(texts.English(null));
    }

    [Fact]
    public void Render_strict_throws_where_production_falls_back()
    {
        //Arrange
        var texts = Create(httpContext: null, new TextOptions { Strict = true });

        //Act
        var exception = Record.Exception(() => texts.Render(Message.Of("common.no-such-key")));

        //Assert
        Assert.IsType<MissingTextException>(exception);
    }

    [Fact]
    public void Render_lenient_falls_back_and_logs_one_warning_per_key_and_language()
    {
        //Arrange
        var logs = new RecordingLoggerProvider();
        var texts = Create(httpContext: null, new TextOptions(), logs);
        var unknown = Message.Of("common.no-such-key", ("path", "/media/private"));

        //Act
        var first = texts.Render(unknown);
        var second = texts.Render(unknown);
        var logged = texts.English(unknown);

        //Assert
        const string expected = "This message was written by another version of the app and can't be shown here (common.no-such-key).";
        Assert.Equal((expected, expected, expected), (first, second, logged));
        var warning = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("common.no-such-key", warning.Message, StringComparison.Ordinal);
        // Keys and value names only: a value is a path, a title or a service's words.
        Assert.DoesNotContain("/media/private", warning.Message, StringComparison.Ordinal);
    }

    // The factory is left undisposed: the logger it makes must keep writing for the whole test.
    private static RequestTexts Create(HttpContext? httpContext, TextOptions options, RecordingLoggerProvider? logs = null) =>
        new(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(options),
            new LoggerFactory([logs ?? new RecordingLoggerProvider()]).CreateLogger<RequestTexts>());
}
