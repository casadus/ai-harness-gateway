using System.Text.Json.Nodes;

namespace AiHarnessGateway;

public sealed class RouteResolver(GatewayConfig config)
{
    private readonly Dictionary<string, RouteConfig> _routes = config.Routes
        .ToDictionary(route => route.Alias, StringComparer.OrdinalIgnoreCase);

    public RouteResolution Resolve(JsonObject requestBody)
    {
        var alias = requestBody["model"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new RouteResolutionException("Request body must include a model alias.");
        }

        if (!_routes.TryGetValue(alias, out var route))
        {
            throw new RouteResolutionException($"Unknown model alias '{alias}'.");
        }

        requestBody["model"] = route.Model;
        return new RouteResolution(alias, route);
    }
}

public sealed record RouteResolution(string Alias, RouteConfig Route);

public sealed class RouteResolutionException(string message) : Exception(message);
