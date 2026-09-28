using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.TestInfrastructure.Logging;

/// <summary>One log call the host made, as <see cref="RecordingLoggerProvider"/> kept it.</summary>
/// <param name="Category">The logger's category, e.g. the full name of <c>GlobalExceptionHandler</c>.</param>
/// <param name="Level">The level it was logged at.</param>
/// <param name="Message">The rendered message, placeholders filled in.</param>
/// <param name="Exception">The exception passed with it, if any.</param>
public sealed record RecordedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception);
