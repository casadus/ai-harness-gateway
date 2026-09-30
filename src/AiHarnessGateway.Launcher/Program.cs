using System.Diagnostics;
using System.Text.Json;

var options = LauncherOptions.Parse(args);
var config = RouteConfigFile.Load(options.ConfigPath);
var harnesses = HarnessDetector.Detect();
var ollama = await OllamaDetector.DetectAsync();
var openRouterKeyPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));

Console.WriteLine("AI Harness Gateway");
Console.WriteLine("==================");
Console.WriteLine();
Console.WriteLine("Regular work startup");
Console.WriteLine("1. Choose a harness.");
Console.WriteLine("2. Choose LOCAL Ollama or CLOUD OpenRouter.");
Console.WriteLine("3. Choose a model alias.");
Console.WriteLine("4. Choose a project folder and start working.");
Console.WriteLine();

Console.WriteLine("Harnesses");
foreach (var harness in harnesses)
{
    var status = harness.IsInstalled ? "installed" : "missing";
    Console.WriteLine($"- {harness.Name}: {status} ({harness.Command})");
    if (!harness.IsInstalled)
    {
        Console.WriteLine($"  Install: {harness.InstallHint}");
    }
}

Console.WriteLine();
Console.WriteLine("Configured model aliases");
foreach (var route in config.Routes)
{
    var destination = route.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
        ? "LOCAL"
        : "CLOUD";
    Console.WriteLine($"- {route.Alias}: {destination} {route.Provider} -> {route.Model}");
}

Console.WriteLine();
Console.WriteLine("Ollama");
if (ollama.IsRunning)
{
    Console.WriteLine("- Service: running");
    if (ollama.Models.Count == 0)
    {
        Console.WriteLine("- Installed models: none reported");
    }
    else
    {
        Console.WriteLine("- Installed models:");
        foreach (var model in ollama.Models)
        {
            Console.WriteLine($"  - {model}");
        }
    }
}
else
{
    Console.WriteLine("- Service: not reachable at http://127.0.0.1:11434");
}

Console.WriteLine("- Recommended local model:");
Console.WriteLine("  ollama pull qwen3.5:9b");

Console.WriteLine();
Console.WriteLine("OpenRouter");
Console.WriteLine(openRouterKeyPresent
    ? "- OPENROUTER_API_KEY: available"
    : "- OPENROUTER_API_KEY: not set");
Console.WriteLine("- Recommended cloud model alias: cloud-claude -> anthropic/claude-sonnet-4.6");
Console.WriteLine("- Get a key and model IDs from https://openrouter.ai/");

Console.WriteLine();
Console.WriteLine("Next");
Console.WriteLine("- Shortcut launches should open this launcher.");
Console.WriteLine("- PowerShell remains available for automation and diagnostics.");

if (!options.NoPause && Environment.UserInteractive)
{
    Console.WriteLine();
    Console.Write("Press Enter to close...");
    Console.ReadLine();
}

internal sealed record LauncherOptions(string ConfigPath, bool NoPause)
{
    public static LauncherOptions Parse(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config", "gateway.routes.example.json");
        var noPause = false;

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--config", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                configPath = args[++index];
            }
            else if (args[index].Equals("--no-pause", StringComparison.OrdinalIgnoreCase))
            {
                noPause = true;
            }
        }

        return new LauncherOptions(configPath, noPause);
    }
}

internal sealed record RouteInfo(string Alias, string Provider, string Model);

internal sealed record RouteConfigFile(IReadOnlyList<RouteInfo> Routes)
{
    public static RouteConfigFile Load(string path)
    {
        if (!File.Exists(path))
        {
            return new RouteConfigFile(Array.Empty<RouteInfo>());
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var routes = new List<RouteInfo>();
        if (document.RootElement.TryGetProperty("routes", out var routeElements))
        {
            foreach (var route in routeElements.EnumerateArray())
            {
                routes.Add(new RouteInfo(
                    ReadString(route, "alias"),
                    ReadString(route, "provider"),
                    ReadString(route, "model")));
            }
        }

        return new RouteConfigFile(routes);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? ""
            : "";
    }
}

internal sealed record HarnessStatus(string Name, string Command, bool IsInstalled, string InstallHint);

internal static class HarnessDetector
{
    private static readonly (string Name, string Command, string InstallHint)[] KnownHarnesses =
    [
        ("Codex CLI", "codex", "Install Codex CLI, then reopen this launcher."),
        ("Claude Code", "claude", "Install Claude Code, then reopen this launcher."),
        ("OpenCode", "opencode", "Install OpenCode, then reopen this launcher."),
        ("GitHub Copilot CLI", "gh", "Install GitHub CLI and the Copilot extension.")
    ];

    public static IReadOnlyList<HarnessStatus> Detect()
    {
        return KnownHarnesses
            .Select(harness => new HarnessStatus(
                harness.Name,
                harness.Command,
                IsOnPath(harness.Command),
                harness.InstallHint))
            .ToArray();
    }

    private static bool IsOnPath(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = command,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(2000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed record OllamaStatus(bool IsRunning, IReadOnlyList<string> Models);

internal static class OllamaDetector
{
    public static async Task<OllamaStatus> DetectAsync()
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            using var response = await client.GetAsync("http://127.0.0.1:11434/api/tags");
            if (!response.IsSuccessStatusCode)
            {
                return new OllamaStatus(false, Array.Empty<string>());
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var models = new List<string>();
            if (document.RootElement.TryGetProperty("models", out var modelElements))
            {
                foreach (var model in modelElements.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var name))
                    {
                        var modelName = name.GetString();
                        if (!string.IsNullOrWhiteSpace(modelName))
                        {
                            models.Add(modelName);
                        }
                    }
                }
            }

            return new OllamaStatus(true, models);
        }
        catch
        {
            return new OllamaStatus(false, Array.Empty<string>());
        }
    }
}
