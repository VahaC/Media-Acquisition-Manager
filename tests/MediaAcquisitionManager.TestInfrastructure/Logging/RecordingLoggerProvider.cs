using System.Collections.Concurrent;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.TestInfrastructure.Logging;

/// <summary>
/// Keeps every entry the host logs, so a test can assert that a line is written at the level the
/// code promises: an Error that ships off the box and carries what an operator matches a report
/// with, or an Information that stays below the alerting threshold.
/// </summary>
/// <remarks>
/// It sits behind the host's own filter rules (appsettings), so what it records is what a real
/// sink would get. A concurrent queue, because requests log from any thread.
/// <para>
/// MAM-47 (ADR 0042) — logs stay English: in the pseudo pass an entry holding a pseudo-language mark throws, after it is
/// recorded, so the code that logged a rendered sentence instead of <c>texts.English(message)</c> fails in the test that
/// drove it (the logging framework rethrows a provider's exception to the caller). An entry that reports another check
/// of the pass failing (EF logs the write guard's exception with the marked value in its text) doesn't throw: that
/// failure is already on its way to the test, and throwing over it would hide it.
/// </para>
/// </remarks>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLogEntry> _entries = new();

    /// <summary>Every entry since the last <see cref="Clear"/>, oldest first.</summary>
    public IReadOnlyList<RecordedLogEntry> Entries => [.. _entries];

    public void Clear() => _entries.Clear();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    // Nothing to release; the entries stay readable after the host shuts down.
    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<RecordedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            entries.Enqueue(new RecordedLogEntry(category, logLevel, message, exception));
            if (TestLanguage.IsPseudo && PseudoText.HasMark(message) && !ReportsAPseudoPassFailure(exception))
                throw new InvalidOperationException(
                    $"{PseudoText.FailurePrefix} {category} logged a rendered sentence; logs stay English (texts.English(message)). The entry: {message}");
        }

        private static bool ReportsAPseudoPassFailure(Exception? exception) => exception switch
        {
            null => false,
            AggregateException aggregate => aggregate.InnerExceptions.Any(ReportsAPseudoPassFailure),
            _ => exception.Message.StartsWith(PseudoText.FailurePrefix, StringComparison.Ordinal)
                || ReportsAPseudoPassFailure(exception.InnerException),
        };
    }
}
