using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Api.Texts;

/// <inheritdoc cref="IRequestTexts"/>
public sealed class RequestTexts(
    IHttpContextAccessor httpContextAccessor,
    IOptions<TextOptions> options,
    ILogger<RequestTexts> logger) : IRequestTexts
{
    /// <summary>
    /// The key and language pairs already logged: a sentence that can't be written as asked is rendered on every read of
    /// its row, and one Warning per pair says it without flooding the log that ships off the box.
    /// </summary>
    private readonly ConcurrentDictionary<(string Key, TextLanguage Language), byte> _reported = new();

    public TextLanguage Language => httpContextAccessor.HttpContext is { } context
        ? RequestLanguage.Of(context, options.Value.Pseudo)
        : options.Value.Pseudo ? TextLanguage.Pseudo : TextLanguage.English;

    [return: NotNullIfNotNull(nameof(message))]
    public string? Render(Message? message) => MessageRenderer.Render(message, Language, options.Value.Strict, Report);

    [return: NotNullIfNotNull(nameof(message))]
    public string? English(Message? message) => MessageRenderer.Render(message, TextLanguage.English, options.Value.Strict, Report);

    private void Report(TextProblem problem)
    {
        if (!_reported.TryAdd((problem.Key, problem.Language), 0))
            return;

        // Warning, so it ships off the box: the reader saw a fallback instead of the sentence. Once per key and language.
        switch (problem.Kind)
        {
            case TextProblemKind.NoTemplate:
                logger.LogWarning(
                    "The server sentence {Key} has no English template, likely a row another version of the app wrote; it was shown as the message-unavailable sentence in {Language}.",
                    problem.Key,
                    problem.Language);
                break;
            case TextProblemKind.MissingTranslation:
                logger.LogWarning(
                    "The server sentence {Key} is missing from its Ukrainian locale file; it was shown in English.",
                    problem.Key);
                break;
            case TextProblemKind.MissingValue:
                logger.LogWarning(
                    "The server sentence {Key} lacks its value {ValueName}; it was shown in {Language} with an ellipsis in its place.",
                    problem.Key,
                    problem.ValueName,
                    problem.Language);
                break;
            case TextProblemKind.Misfit:
                logger.LogWarning(
                    "The server sentence {Key} has a value that doesn't fit its placeholder ({Detail}); it was shown in {Language} as the value reads.",
                    problem.Key,
                    problem.Detail,
                    problem.Language);
                break;
            default:
                logger.LogWarning(
                    "Rendering the server sentence {Key} in {Language} threw {ExceptionType}; the nearest fallback was shown.",
                    problem.Key,
                    problem.Language,
                    problem.Detail);
                break;
        }
    }
}
