using System.Diagnostics.CodeAnalysis;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — writes the server's messages in the language of the request being served (Facade over
/// <see cref="MessageRenderer"/>, Ambient Context through <c>IHttpContextAccessor</c>). A singleton, so the mappers, also
/// singletons, take it in their constructors and no service lifetime changes.
/// </summary>
public interface IRequestTexts
{
    /// <summary>
    /// The current request's language (<see cref="RequestLanguage"/>); outside a request English — or the
    /// pseudo-language when <c>Texts:Pseudo</c> is on, so a background render in the test suite's second pass shows up
    /// in the database guard.
    /// </summary>
    TextLanguage Language { get; }

    /// <summary><paramref name="message"/> in <see cref="Language"/>: DTO fields, error bodies, MCP answers, health.
    /// <c>null</c> for <c>null</c>.</summary>
    [return: NotNullIfNotNull(nameof(message))]
    string? Render(Message? message);

    /// <summary>
    /// <paramref name="message"/> in English, never wrapped: logs, and rules measured in one fixed language (the
    /// audit name's 200 characters). <c>null</c> for <c>null</c>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(message))]
    string? English(Message? message);
}
