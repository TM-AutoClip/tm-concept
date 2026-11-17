using Microsoft.Extensions.Logging;

namespace TMConcept.Tests.Documents;

/// <summary>Keeps every log line with its exception text, so a test can check that no secret reached the log.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public string All => string.Join("\n", Lines);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Lines.Add($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}
