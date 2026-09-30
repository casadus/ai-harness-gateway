using AiHarnessGateway;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

var tests = new (string Name, Action Test)[]
{
    ("duplicate aliases are rejected", RejectsDuplicateAliases),
    ("non-loopback listen URLs are rejected", RejectsNonLoopbackListenUrls),
    ("missing model aliases are rejected", RejectsMissingModelAliases),
    ("missing cloud keys are rejected", RejectsMissingCloudKeys),
    ("safe diagnostics persist metadata and rotate", PersistsAndRotatesSafeDiagnostics)
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

if (failures > 0)
{
    Environment.Exit(1);
}

static void RejectsDuplicateAliases()
{
    var config = new GatewayConfig
    {
        Routes =
        [
            TestRoute("same"),
            TestRoute("same")
        ]
    };

    ExpectInvalidOperation(() => config.Validate(), "Duplicate route alias 'same'.");
}

static void RejectsNonLoopbackListenUrls()
{
    var config = new GatewayConfig
    {
        ListenUrl = "http://0.0.0.0:5872",
        Routes = [TestRoute("local")]
    };

    ExpectInvalidOperation(() => config.Validate(), "Gateway must listen on an absolute 127.0.0.1 URL.");
}

static void RejectsMissingModelAliases()
{
    var resolver = new RouteResolver(new GatewayConfig
    {
        Routes = [TestRoute("local-qwen")]
    });

    ExpectRouteResolution(() => resolver.Resolve(new JsonObject()), "Request body must include a model alias.");
}

static void RejectsMissingCloudKeys()
{
    const string envName = "AI_HARNESS_GATEWAY_TEST_KEY";
    Environment.SetEnvironmentVariable(envName, null);
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/v1/chat/completions");

    var route = TestRoute("cloud-test") with
    {
        AuthEnvironmentVariable = envName
    };

    ExpectInvalidOperation(
        () => UpstreamAuthentication.Apply(request, route),
        "Route 'cloud-test' requires environment variable 'AI_HARNESS_GATEWAY_TEST_KEY'.");
}

static void PersistsAndRotatesSafeDiagnostics()
{
    var directory = Path.Combine(Path.GetTempPath(), "ai-harness-gateway-tests", Guid.NewGuid().ToString("N"));
    var logPath = Path.Combine(directory, "gateway.ndjson");
    Directory.CreateDirectory(directory);

    try
    {
        File.WriteAllText(logPath, new string('x', 1200));

        var diagnostics = new SafeDiagnostics(
            new GatewayConfig
            {
                Diagnostics = new DiagnosticsConfig
                {
                    LogFilePath = logPath,
                    MaxLogBytes = 1024,
                    RetainedLogFiles = 1
                },
                Routes = [TestRoute("local-qwen")]
            },
            new NullLogger<SafeDiagnostics>());

        diagnostics.Record(new DiagnosticEvent(
            DateTimeOffset.Parse("2026-09-30T00:00:00Z"),
            "/v1/chat/completions",
            "local-qwen",
            "ollama",
            "qwen3.5:9b",
            400,
            12,
            "route-error"));

        if (!File.Exists($"{logPath}.1"))
        {
            throw new InvalidOperationException("Expected rotated log file was not created.");
        }

        var currentLog = File.ReadAllText(logPath);
        foreach (var expected in new[]
        {
            "\"alias\":\"local-qwen\"",
            "\"provider\":\"ollama\"",
            "\"model\":\"qwen3.5:9b\"",
            "\"status\":400",
            "\"category\":\"route-error\""
        })
        {
            if (!currentLog.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected diagnostic log to contain {expected}.");
            }
        }

        if (currentLog.Contains("messages", StringComparison.OrdinalIgnoreCase) ||
            currentLog.Contains("Authorization", StringComparison.OrdinalIgnoreCase) ||
            currentLog.Contains("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Diagnostic log contains unsafe request or credential text.");
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static RouteConfig TestRoute(string alias)
{
    return new RouteConfig
    {
        Alias = alias,
        Provider = "ollama",
        Protocol = "openai",
        Model = "qwen3.5:9b",
        BaseUrl = "http://127.0.0.1:11434/v1"
    };
}

static void ExpectInvalidOperation(Action action, string expectedMessage)
{
    ExpectException<InvalidOperationException>(action, expectedMessage);
}

static void ExpectRouteResolution(Action action, string expectedMessage)
{
    ExpectException<RouteResolutionException>(action, expectedMessage);
}

static void ExpectException<TException>(Action action, string expectedMessage)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException ex) when (ex.Message == expectedMessage)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name} with message: {expectedMessage}");
}

internal sealed class NullLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return false;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
