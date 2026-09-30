using System.Text.Json;

namespace AiHarnessGateway;

public sealed class SafeDiagnostics(GatewayConfig config, ILogger<SafeDiagnostics> logger)
{
    private const int Capacity = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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

            TryAppendToLog(diagnostic);
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

    private void TryAppendToLog(DiagnosticEvent diagnostic)
    {
        try
        {
            var logPath = Path.GetFullPath(config.Diagnostics.LogFilePath);
            var logDirectory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            RotateIfNeeded(logPath);

            var line = JsonSerializer.Serialize(diagnostic, JsonOptions);
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write safe diagnostic log.");
        }
    }

    private void RotateIfNeeded(string logPath)
    {
        var logInfo = new FileInfo(logPath);
        if (!logInfo.Exists || logInfo.Length < config.Diagnostics.MaxLogBytes)
        {
            return;
        }

        for (var index = config.Diagnostics.RetainedLogFiles; index >= 1; index--)
        {
            var source = $"{logPath}.{index}";
            var destination = $"{logPath}.{index + 1}";

            if (!File.Exists(source))
            {
                continue;
            }

            if (index == config.Diagnostics.RetainedLogFiles)
            {
                File.Delete(source);
            }
            else
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        if (config.Diagnostics.RetainedLogFiles > 0)
        {
            File.Move(logPath, $"{logPath}.1", overwrite: true);
        }
        else
        {
            File.Delete(logPath);
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
