using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiHarnessGateway;

public sealed class GatewayProxy(
    IHttpClientFactory httpClientFactory,
    RouteResolver routeResolver,
    SafeDiagnostics diagnostics)
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Content-Length",
        "Expect",
        "Host",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Authorization"
    };

    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        "Content-Encoding",
        "Content-Language",
        "Content-Location",
        "Content-MD5",
        "Content-Range",
        "Expires",
        "Last-Modified"
    };

    public async Task ForwardAsync(HttpContext context)
    {
        var timer = Stopwatch.StartNew();
        RouteResolution? resolution = null;
        var status = StatusCodes.Status502BadGateway;
        var category = "upstream-error";

        try
        {
            var requestBody = await ReadBodyAsync(context.Request, context.RequestAborted);
            resolution = routeResolver.Resolve(requestBody);
            var upstreamBody = requestBody.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var upstreamUri = BuildUpstreamUri(resolution.Route, context.Request.Path);

            using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, upstreamUri)
            {
                Content = new StringContent(upstreamBody, Encoding.UTF8, "application/json")
            };

            CopyRequestHeaders(context.Request, upstreamRequest);
            UpstreamAuthentication.Apply(upstreamRequest, resolution.Route);

            var client = httpClientFactory.CreateClient(nameof(GatewayProxy));
            using var upstreamResponse = await client.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            status = (int)upstreamResponse.StatusCode;
            category = upstreamResponse.IsSuccessStatusCode ? "ok" : "upstream-error";
            await CopyResponseAsync(context, upstreamResponse, context.RequestAborted);
        }
        catch (RouteResolutionException ex)
        {
            status = StatusCodes.Status400BadRequest;
            category = "route-error";
            await WriteErrorAsync(context, status, ex.Message, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            status = 499;
            category = "cancelled";
        }
        catch (Exception ex)
        {
            status = StatusCodes.Status502BadGateway;
            category = "gateway-error";
            await WriteErrorAsync(context, status, ex.Message, context.RequestAborted);
        }
        finally
        {
            timer.Stop();
            diagnostics.Record(new DiagnosticEvent(
                DateTimeOffset.UtcNow,
                context.Request.Path,
                resolution?.Alias,
                resolution?.Route.Provider,
                resolution?.Route.Model,
                status,
                timer.ElapsedMilliseconds,
                category));
        }
    }

    private static async Task<JsonObject> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var body = await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken);
        return body as JsonObject
            ?? throw new RouteResolutionException("Request body must be a JSON object.");
    }

    private static Uri BuildUpstreamUri(RouteConfig route, PathString requestPath)
    {
        var baseUri = route.BaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? route.BaseUrl[..^1]
            : route.BaseUrl;
        var path = requestPath.Value ?? string.Empty;
        if (baseUri.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
            path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            path = path[3..];
        }

        return new Uri($"{baseUri}{path}");
    }

    private static void CopyRequestHeaders(HttpRequest source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            var values = header.Value.ToArray();
            if (ContentHeaders.Contains(header.Key))
            {
                target.Content?.Headers.TryAddWithoutValidation(header.Key, values);
            }
            else
            {
                target.Headers.TryAddWithoutValidation(header.Key, values);
            }
        }
    }

    private static async Task CopyResponseAsync(
        HttpContext context,
        HttpResponseMessage upstreamResponse,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;

        foreach (var header in upstreamResponse.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        context.Response.Headers.Remove("transfer-encoding");
        await upstreamResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static Task WriteErrorAsync(
        HttpContext context,
        int status,
        string message,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                message,
                type = "gateway_error"
            }
        }, cancellationToken);
    }
}
