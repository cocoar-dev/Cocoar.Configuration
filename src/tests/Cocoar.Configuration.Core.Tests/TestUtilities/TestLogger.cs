using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Core.Tests.TestUtilities;

/// <summary>
/// A test logger that captures log entries for assertion.
/// </summary>
public class TestLogger : ILogger
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList().AsReadOnly();
            }
        }
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        lock (_lock)
        {
            _entries.Add(new LogEntry(logLevel, eventId, message, exception));
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool HasLogEntry(LogLevel level, string messageContains)
    {
        return Entries.Any(e => e.Level == level && e.Message.Contains(messageContains, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasLogEntry(LogLevel level, int eventId)
    {
        return Entries.Any(e => e.Level == level && e.EventId.Id == eventId);
    }

    public LogEntry? FindEntry(LogLevel level, string messageContains)
    {
        return Entries.FirstOrDefault(e => e.Level == level && e.Message.Contains(messageContains, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}

public record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
