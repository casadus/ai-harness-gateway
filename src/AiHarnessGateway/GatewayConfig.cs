using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiHarnessGateway;

public sealed record GatewayConfig
{
    public string ListenUrl { get; init; } = "http://127.0.0.1:5872";
    public RouteConfig[] Routes { get; init; } = GatewayDefaults.Routes;

    public void Validate()
    {
        if (!Uri.TryCreate(ListenUrl, UriKind.Absolute, out var listenUri) ||
            !string.Equals(listenUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Gateway must listen on an absolute 127.0.0.1 URL.");
        }

        if (Routes.Length == 0)
        {
            throw new InvalidOperationException("At least one route is required.");
        }

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in Routes)
        {
            route.Validate();
            if (!aliases.Add(route.Alias))
            {
                throw new InvalidOperationException($"Duplicate route alias '{route.Alias}'.");
            }
        }
    }
}

public sealed record RouteConfig
{
    public required string Alias { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required string BaseUrl { get; init; }
    public string? AuthEnvironmentVariable { get; init; }
    public string Protocol { get; init; } = "openai";

    [JsonIgnore]
    public bool RequiresBearerToken => !string.IsNullOrWhiteSpace(AuthEnvironmentVariable);

    public void Validate()
    {
        Require(Alias, nameof(Alias));
        Require(Provider, nameof(Provider));
        Require(Model, nameof(Model));
        Require(BaseUrl, nameof(BaseUrl));
        Require(Protocol, nameof(Protocol));

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Route '{Alias}' has an invalid baseUrl.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"Route '{Alias}' baseUrl must be HTTP or HTTPS.");
        }
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Route {name} is required.");
        }
    }
}

public static class GatewayConfigLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static GatewayConfig Load(string[] args)
    {
        var path = FindConfigPath(args);
        if (path is null)
        {
            return new GatewayConfig();
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Gateway config file was not found.", path);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GatewayConfig>(json, Options)
            ?? throw new InvalidOperationException("Gateway config file was empty.");
    }

    private static string? FindConfigPath(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--config", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        var environmentPath = Environment.GetEnvironmentVariable("AI_HARNESS_GATEWAY_CONFIG");
        return string.IsNullOrWhiteSpace(environmentPath) ? null : environmentPath;
    }
}

internal static class GatewayDefaults
{
    public static readonly RouteConfig[] Routes =
    [
        new()
        {
            Alias = "local-qwen",
            Provider = "ollama",
            Protocol = "openai",
            Model = "qwen3.5:9b",
            BaseUrl = "http://127.0.0.1:11434/v1"
        },
        new()
        {
            Alias = "cloud-claude",
            Provider = "openrouter",
            Protocol = "openai",
            Model = "anthropic/claude-sonnet-4.6",
            BaseUrl = "https://openrouter.ai/api/v1",
            AuthEnvironmentVariable = "OPENROUTER_API_KEY"
        }
    ];
}
