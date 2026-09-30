namespace AiHarnessGateway;

public sealed class SafeDiagnostics(ILogger<SafeDiagnostics> logger)
{
    private const int Capacity = 50;
    private readonly Queue<DiagnosticEvent> _recent = new();
    private readonly object _sync = new();

    public void Record(DiagnosticEvent diagnostic)
    {
        lock (_sync)
        {
            _recent.Enqueue(diagnostic);
            while (_recent.Count > Capacity)
            {
                _recent.Dequeue();
            }
        }

        logger.LogInformation(
            "gateway route={Alias} provider={Provider} model={Model} status={Status} durationMs={DurationMs} category={Category}",
            diagnostic.Alias,
            diagnostic.Provider,
            diagnostic.Model,
            diagnostic.Status,
            diagnostic.DurationMs,
            diagnostic.Category);
    }

    public DiagnosticEvent[] Recent()
    {
        lock (_sync)
        {
            return _recent.ToArray();
        }
    }
}

public sealed record DiagnosticEvent(
    DateTimeOffset Timestamp,
    string Path,
    string? Alias,
    string? Provider,
    string? Model,
    int Status,
    long DurationMs,
    string Category);
