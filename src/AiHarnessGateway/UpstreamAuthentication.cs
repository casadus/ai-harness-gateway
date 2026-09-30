using System.Net.Http.Headers;

namespace AiHarnessGateway;

public static class UpstreamAuthentication
{
    public static void Apply(HttpRequestMessage request, RouteConfig route)
    {
        if (!route.RequiresBearerToken)
        {
            return;
        }

        var token = Environment.GetEnvironmentVariable(route.AuthEnvironmentVariable!);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"Route '{route.Alias}' requires environment variable '{route.AuthEnvironmentVariable}'.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
