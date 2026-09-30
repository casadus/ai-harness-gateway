using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

if (args.Length == 0 || !int.TryParse(args[0], out var port))
{
    Console.Error.WriteLine("Usage: FakeUpstream <port>");
    return 2;
}

var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
Console.WriteLine($"Fake upstream listening on http://127.0.0.1:{port}/");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleAsync(client));
}

static async Task HandleAsync(TcpClient client)
{
    await using var stream = client.GetStream();
    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

    var requestLine = await reader.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(requestLine))
    {
        return;
    }

    var requestParts = requestLine.Split(' ', 3);
    var method = requestParts[0];
    var path = requestParts.Length > 1 ? requestParts[1] : "/";
    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    string? line;
    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
    {
        var separator = line.IndexOf(':');
        if (separator > 0)
        {
            headers[line[..separator]] = line[(separator + 1)..].Trim();
        }
    }

    if (method == "GET")
    {
        await WriteResponseAsync(stream, 200, "text/plain", "ok");
        return;
    }

    if (method != "POST")
    {
        await WriteResponseAsync(stream, 404, "text/plain", "not found");
        return;
    }

    var contentLength = headers.TryGetValue("Content-Length", out var contentLengthText) &&
        int.TryParse(contentLengthText, out var parsedLength)
        ? parsedLength
        : 0;

    var buffer = new char[contentLength];
    var read = 0;
    while (read < contentLength)
    {
        var current = await reader.ReadAsync(buffer, read, contentLength - read);
        if (current == 0)
        {
            break;
        }

        read += current;
    }

    var requestText = new string(buffer, 0, read);
    var body = JsonNode.Parse(requestText) as JsonObject
        ?? throw new InvalidOperationException("Request body must be a JSON object.");

    if (body["stream"]?.GetValue<bool>() == true)
    {
        await WriteStreamAsync(stream);
        return;
    }

    var response = new JsonObject
    {
        ["path"] = path,
        ["model"] = body["model"]?.GetValue<string>(),
        ["authorization"] = headers.TryGetValue("Authorization", out var authorization) ? authorization : null,
        ["firstToolName"] = body["tools"]?[0]?["function"]?["name"]?.GetValue<string>(),
        ["contentType"] = headers.TryGetValue("Content-Type", out var contentType) ? contentType : null
    };

    await WriteResponseAsync(stream, 202, "application/json", response.ToJsonString());
}

static async Task WriteResponseAsync(Stream stream, int status, string contentType, string body)
{
    var bodyBytes = Encoding.UTF8.GetBytes(body);
    var responseHead = string.Join("\r\n", new[]
    {
        $"HTTP/1.1 {status} {ReasonPhrase(status)}",
        $"Content-Type: {contentType}",
        $"Content-Length: {bodyBytes.Length}",
        "Connection: close",
        "",
        ""
    });

    await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHead));
    await stream.WriteAsync(bodyBytes);
}

static async Task WriteStreamAsync(Stream stream)
{
    var responseHead = string.Join("\r\n", new[]
    {
        "HTTP/1.1 200 OK",
        "Content-Type: text/event-stream",
        "Transfer-Encoding: chunked",
        "Connection: close",
        "",
        ""
    });

    await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHead));

    foreach (var chunk in new[] { "data: one\n\n", "data: two\n\n" })
    {
        var chunkBytes = Encoding.UTF8.GetBytes(chunk);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{chunkBytes.Length:X}\r\n"));
        await stream.WriteAsync(chunkBytes);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
        await stream.FlushAsync();
        await Task.Delay(50);
    }

    await stream.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"));
}

static string ReasonPhrase(int status)
{
    return status switch
    {
        200 => "OK",
        202 => "Accepted",
        404 => "Not Found",
        _ => "Status"
    };
}
