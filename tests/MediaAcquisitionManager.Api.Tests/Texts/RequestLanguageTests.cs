using MediaAcquisitionManager.Api.Texts;
using Microsoft.AspNetCore.Http;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the request's language is its Accept-Language reduced to en or uk, else English; nothing about
// the caller (session, token, Users.Language) is read, so every 401 and sign-in failure stays the same bytes for every
// cause. The resolution rules over a bare request; the HTTP proof over the real pipeline is RequestLanguageHttpTests.
public sealed class RequestLanguageTests
{
    [Theory]
    [InlineData("uk", false, TextLanguage.Ukrainian)]
    [InlineData("uk-UA", false, TextLanguage.Ukrainian)]
    [InlineData("UK-ua", false, TextLanguage.Ukrainian)]
    [InlineData("en-GB", false, TextLanguage.English)]
    [InlineData("en", false, TextLanguage.English)]
    [InlineData("de, uk;q=0.5", false, TextLanguage.Ukrainian)]
    [InlineData("en;q=0.4, uk;q=0.8", false, TextLanguage.Ukrainian)]
    [InlineData("uk;q=0.5, en;q=0.5", false, TextLanguage.Ukrainian)]
    [InlineData("en;q=0.5, uk;q=0.5", false, TextLanguage.English)]
    [InlineData("de-DE, fr;q=0.9, uk-UA;q=0.8, en;q=0.7", false, TextLanguage.Ukrainian)]
    [InlineData("de", false, TextLanguage.English)]
    [InlineData("", false, TextLanguage.English)]
    [InlineData("*", false, TextLanguage.English)]
    [InlineData("uk;q=0", false, TextLanguage.English)]
    [InlineData("uk;q=0, de", false, TextLanguage.English)]
    [InlineData(";;;,,,", false, TextLanguage.English)]
    [InlineData("uk;q=abc, en-US", false, TextLanguage.English)]
    [InlineData("qps-ploc", false, TextLanguage.English)]
    [InlineData("qps-ploc", true, TextLanguage.Pseudo)]
    [InlineData("qps-ploc, uk", false, TextLanguage.Ukrainian)]
    [InlineData("uk", true, TextLanguage.Ukrainian)]
    public void Of_reduces_accept_language_to_a_language_of_the_server(string header, bool pseudoAllowed, TextLanguage expected)
    {
        //Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = header;

        //Act
        var language = RequestLanguage.Of(context, pseudoAllowed);

        //Assert
        Assert.Equal(expected, language);
    }

    [Fact]
    public void Of_a_request_without_the_header_is_english()
    {
        //Arrange
        var context = new DefaultHttpContext();

        //Act
        var language = RequestLanguage.Of(context, pseudoAllowed: true);

        //Assert
        Assert.Equal(TextLanguage.English, language);
    }

    [Fact]
    public void Of_works_the_language_out_once_per_request()
    {
        //Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "uk";
        var first = RequestLanguage.Of(context, pseudoAllowed: false);
        context.Request.Headers.AcceptLanguage = "en";

        //Act
        var second = RequestLanguage.Of(context, pseudoAllowed: false);

        //Assert
        Assert.Equal((TextLanguage.Ukrainian, TextLanguage.Ukrainian), (first, second));
    }
}
