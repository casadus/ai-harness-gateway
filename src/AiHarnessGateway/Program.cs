using AiHarnessGateway;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
});

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var gatewayConfig = GatewayConfigLoader.Load(args);
gatewayConfig.Validate();

builder.WebHost.UseUrls(gatewayConfig.ListenUrl);
builder.Services.AddSingleton(gatewayConfig);
builder.Services.AddSingleton<RouteResolver>();
builder.Services.AddSingleton<SafeDiagnostics>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<GatewayProxy>();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/healthz"));

app.MapGet("/healthz", (GatewayConfig config, SafeDiagnostics diagnostics) =>
{
    return Results.Ok(new
    {
        status = "ok",
        listenUrl = config.ListenUrl,
        routes = config.Routes.Select(route => new
        {
            route.Alias,
            route.Provider,
            route.Protocol,
            route.Model,
            route.BaseUrl
        }),
        recent = diagnostics.Recent()
    });
});

app.MapPost("/v1/responses", ProxyRequest);
app.MapPost("/v1/chat/completions", ProxyRequest);
app.MapPost("/v1/messages", ProxyRequest);

app.Run();

static Task ProxyRequest(HttpContext context, GatewayProxy proxy)
{
    return proxy.ForwardAsync(context);
}
