using System.Diagnostics.CodeAnalysis;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a <see cref="Message"/> as a reader sees it, for the tests of Core, Infrastructure and stored
/// rows that assert today's English: <c>Assert.Equal("Couldn't reach …", TestTexts.En(result.Error))</c>. Strict, over
/// the embedded locale files, so a missing key, a missing or unused value, or a value that doesn't fit its placeholder
/// fails the test that reached it.
/// </summary>
public static class TestTexts
{
    /// <summary><paramref name="message"/> in English; <c>null</c> for <c>null</c>.</summary>
    [return: NotNullIfNotNull(nameof(message))]
    public static string? En(Message? message) => MessageRenderer.Render(message, TextLanguage.English, strict: true);

    /// <summary><paramref name="message"/> in Ukrainian (English for a key whose Ukrainian file hasn't landed); <c>null</c>
    /// for <c>null</c>.</summary>
    [return: NotNullIfNotNull(nameof(message))]
    public static string? Uk(Message? message) => MessageRenderer.Render(message, TextLanguage.Ukrainian, strict: true);
}
