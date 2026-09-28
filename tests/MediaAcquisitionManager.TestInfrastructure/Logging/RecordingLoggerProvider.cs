using System.Collections.Concurrent;
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
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new RecordedLogEntry(category, logLevel, formatter(state, exception), exception));
    }
}
