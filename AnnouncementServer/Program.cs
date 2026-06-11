using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(
    builder.Configuration["AnnouncementServer:Urls"] ??
    "http://0.0.0.0:5088");
builder.Services.AddSingleton<AnnouncementBroker>();

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet(
    "/api/announcements/current",
    (AnnouncementBroker broker) =>
        Results.Text(broker.CurrentJson, "application/json", Encoding.UTF8));

app.MapPost(
    "/api/announcements",
    async (
        HttpRequest request,
        AnnouncementBroker broker,
        IConfiguration configuration,
        CancellationToken token) =>
    {
        string expectedToken =
            Environment.GetEnvironmentVariable(
                "SX3_ANNOUNCEMENT_ADMIN_TOKEN") ??
            configuration["AnnouncementServer:AdminToken"] ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return Results.Problem(
                "Announcement publishing is disabled because no admin token is configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        string authorization = request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(
                bearerPrefix,
                StringComparison.OrdinalIgnoreCase) ||
            !TokensEqual(
                authorization.Substring(bearerPrefix.Length).Trim(),
                expectedToken))
        {
            return Results.Unauthorized();
        }

        try
        {
            using var reader = new StreamReader(
                request.Body,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 8192,
                leaveOpen: true);
            string json = await reader.ReadToEndAsync(token);
            string normalized = AnnouncementBroker.ValidateAndNormalize(json);
            await broker.PublishAsync(normalized, token);
            return Results.Ok(new
            {
                published = true,
                clients = broker.ConnectedClientCount,
                utc = DateTimeOffset.UtcNow
            });
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new
            {
                error = "Invalid announcement JSON.",
                detail = ex.Message
            });
        }
        catch (InvalidDataException ex)
        {
            return Results.BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (DecoderFallbackException)
        {
            return Results.BadRequest(new
            {
                error = "Announcement payload is not valid UTF-8."
            });
        }
    });

app.Map(
    "/ws/announcements",
    async (
        HttpContext context,
        AnnouncementBroker broker,
        CancellationToken token) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket socket =
            await context.WebSockets.AcceptWebSocketAsync();
        await broker.RunClientAsync(socket, token);
    });

app.Run();

static bool TokensEqual(string actual, string expected)
{
    byte[] actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);
    byte[] expectedBytes = Encoding.UTF8.GetBytes(expected ?? string.Empty);
    return actualBytes.Length == expectedBytes.Length &&
        CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
}

internal sealed class AnnouncementBroker
{
    private const int MaximumAnnouncementBytes = 256 * 1024;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly string _announcementPath;
    private string _currentJson;

    public AnnouncementBroker(IWebHostEnvironment environment)
    {
        _announcementPath = Path.Combine(
            environment.ContentRootPath,
            "announcement.json");

        string initialJson = File.Exists(_announcementPath)
            ? File.ReadAllText(_announcementPath, Encoding.UTF8)
            : "{\"enabled\":false,\"mode\":\"single\",\"message\":\"\"}";
        _currentJson = ValidateAndNormalize(initialJson);
        File.WriteAllText(
            _announcementPath,
            _currentJson,
            new UTF8Encoding(false));
    }

    public string CurrentJson => Volatile.Read(ref _currentJson);

    public int ConnectedClientCount => _clients.Count;

    public async Task RunClientAsync(
        WebSocket socket,
        CancellationToken token)
    {
        Guid clientId = Guid.NewGuid();
        _clients[clientId] = socket;

        try
        {
            await SendAsync(socket, CurrentJson, token);
            var buffer = new byte[1024];

            while (socket.State == WebSocketState.Open &&
                   !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            if (socket.State == WebSocketState.Open ||
                socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    public async Task PublishAsync(string json, CancellationToken token)
    {
        await _publishLock.WaitAsync(token);
        try
        {
            string tempPath = _announcementPath + ".tmp";
            await File.WriteAllTextAsync(
                tempPath,
                json,
                new UTF8Encoding(false),
                token);
            File.Move(tempPath, _announcementPath, overwrite: true);
            Volatile.Write(ref _currentJson, json);
        }
        finally
        {
            _publishLock.Release();
        }

        await BroadcastAsync(json, token);
    }

    public static string ValidateAndNormalize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Announcement JSON is empty.");
        if (Encoding.UTF8.GetByteCount(json) > MaximumAnnouncementBytes)
            throw new InvalidDataException("Announcement JSON is too large.");

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "Announcement JSON root must be an object.");
        if (ContainsInvalidEncoding(document.RootElement))
            throw new InvalidDataException(
                "Announcement JSON contains invalid cached encoding.");

        return JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });
    }

    private static bool ContainsInvalidEncoding(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (ContainsInvalidEncoding(property.Value))
                        return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (ContainsInvalidEncoding(item))
                        return true;
                }
                return false;
            case JsonValueKind.String:
                return ContainsInvalidEncoding(element.GetString());
            default:
                return false;
        }
    }

    private static bool ContainsInvalidEncoding(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        string[] markers =
        {
            "\uFFFD", "THÃ", "Sáº", "áº", "á»", "Ä‘", "Ä\u0090",
            "Æ°", "Æ¡", "Ã´", "Ã¡", "Ã¢", "Ãª", "Ã©", "Ã¨", "ðŸ"
        };
        foreach (char character in value)
        {
            if (character >= '\u0080' && character <= '\u009F')
                return true;
        }

        return markers.Any(
            marker => value.Contains(marker, StringComparison.Ordinal));
    }

    private async Task BroadcastAsync(string json, CancellationToken token)
    {
        foreach (KeyValuePair<Guid, WebSocket> client in _clients.ToArray())
        {
            try
            {
                await SendAsync(client.Value, json, token);
            }
            catch
            {
                _clients.TryRemove(client.Key, out _);
                try
                {
                    client.Value.Abort();
                    client.Value.Dispose();
                }
                catch
                {
                }
            }
        }
    }

    private static Task SendAsync(
        WebSocket socket,
        string json,
        CancellationToken token)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(
            new ArraySegment<byte>(payload),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken: token);
    }
}
