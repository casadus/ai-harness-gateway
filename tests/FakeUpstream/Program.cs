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

    if (body["model"]?.GetValue<string>() == "rate-limited-model")
    {
        await WriteResponseAsync(stream, 429, "application/json", "{\"error\":{\"message\":\"simulated rate limit\"}}");
        return;
    }

    if (body["stream"]?.GetValue<bool>() == true)
    {
        if (path.EndsWith("/responses", StringComparison.Ordinal))
        {
            await WriteResponsesStreamAsync(stream, body["model"]?.GetValue<string>() ?? "fake-model");
        }
        else
        {
            await WriteStreamAsync(stream, body["model"]?.GetValue<string>() == "slow-model");
        }
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

static async Task WriteStreamAsync(Stream stream, bool slow)
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
        await Task.Delay(slow ? 2000 : 50);
    }

    await stream.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"));
}

static async Task WriteResponsesStreamAsync(Stream stream, string model)
{
    var response = new
    {
        id = "resp_fake_1",
        @object = "response",
        created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        status = "completed",
        error = (object?)null,
        incomplete_details = (object?)null,
        instructions = (object?)null,
        max_output_tokens = (int?)null,
        model,
        output = new[]
        {
            new
            {
                id = "msg_fake_1",
                type = "message",
                status = "completed",
                role = "assistant",
                content = new[] { new { type = "output_text", text = "hello", annotations = Array.Empty<object>() } }
            }
        },
        parallel_tool_calls = true,
        previous_response_id = (string?)null,
        reasoning = new { effort = (string?)null, summary = (string?)null },
        store = false,
        temperature = 1.0,
        text = new { format = new { type = "text" } },
        tool_choice = "auto",
        tools = Array.Empty<object>(),
        top_p = 1.0,
        truncation = "disabled",
        usage = new
        {
            input_tokens = 1,
            output_tokens = 1,
            total_tokens = 2,
            input_tokens_details = new { cached_tokens = 0 },
            output_tokens_details = new { reasoning_tokens = 0 }
        },
        user = (string?)null,
        metadata = new { }
    };
    var itemId = "msg_fake_1";
    var completedItem = response.output[0];
    var completedPart = completedItem.content[0];
    var events = new (string Name, object Data)[]
    {
        ("response.output_item.added", new
        {
            type = "response.output_item.added", output_index = 0,
            item = new { id = itemId, type = "message", status = "in_progress", role = "assistant", content = Array.Empty<object>() },
            sequence_number = 1
        }),
        ("response.content_part.added", new
        {
            type = "response.content_part.added", item_id = itemId, output_index = 0, content_index = 0,
            part = new { type = "output_text", text = "", annotations = Array.Empty<object>(), logprobs = Array.Empty<object>() },
            sequence_number = 2
        }),
        ("response.output_text.delta", new
        {
            type = "response.output_text.delta", item_id = itemId, output_index = 0, content_index = 0,
            delta = "hello", sequence_number = 3, logprobs = Array.Empty<object>()
        }),
        ("response.output_text.done", new
        {
            type = "response.output_text.done", item_id = itemId, output_index = 0, content_index = 0,
            text = "hello", sequence_number = 4, logprobs = Array.Empty<object>()
        }),
        ("response.content_part.done", new
        {
            type = "response.content_part.done", item_id = itemId, output_index = 0, content_index = 0,
            part = completedPart, sequence_number = 5
        }),
        ("response.output_item.done", new
        {
            type = "response.output_item.done", output_index = 0, item = completedItem, sequence_number = 6
        }),
        ("response.completed", new { type = "response.completed", response, sequence_number = 7 })
    };
    var eventText = string.Concat(events.Select(entry =>
        $"event: {entry.Name}\ndata: {System.Text.Json.JsonSerializer.Serialize(entry.Data)}\n\n"));
    var chunk = Encoding.UTF8.GetBytes(eventText);
    var responseHead = string.Join("\r\n", new[]
    {
        "HTTP/1.1 200 OK",
        "Content-Type: text/event-stream",
        $"Content-Length: {chunk.Length}",
        "Connection: close",
        "",
        ""
    });

    await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHead));
    await stream.WriteAsync(chunk);
}

static string ReasonPhrase(int status)
{
    return status switch
    {
        200 => "OK",
        202 => "Accepted",
        404 => "Not Found",
        429 => "Too Many Requests",
        _ => "Status"
    };
}
