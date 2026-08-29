using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RoImportBridge;

internal sealed class BridgeServer : IDisposable
{
    private const string Host = "127.0.0.1";
    private const int Port = 27123;
    private const int BridgeVersion = 3;
    private const int MaxBodySize = 32 * 1024 * 1024;
    private const string RobloxAssetUrl = "https://apis.roblox.com/assets/v1/assets";
    private const string RobloxOperationUrl = "https://apis.roblox.com/assets/v1/operations";
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly HttpClient httpClient = new();
    private readonly UploadLogStore logStore;
    private TcpListener? listener;

    public event Action<string>? StatusChanged;

    public BridgeServer(UploadLogStore logStore)
    {
        this.logStore = logStore;
    }

    public async Task StartAsync()
    {
        try
        {
            listener = new TcpListener(IPAddress.Parse(Host), Port);
            listener.Start();
            StatusChanged?.Invoke($"Running at http://localhost:{Port}");
            await AcceptClientsAsync(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            StatusChanged?.Invoke($"Bridge stopped: {error.Message}");
        }
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener!.AcceptTcpClientAsync(cancellationToken);
            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var request = await LocalHttpRequest.ReadAsync(stream, MaxBodySize, cancellationToken);
                await RouteRequestAsync(request, stream, cancellationToken);
            }
            catch (Exception error)
            {
                await LocalHttpResponse.WriteJsonAsync(stream, 400, new { error = error.Message }, cancellationToken);
            }
        }
    }

    private async Task RouteRequestAsync(LocalHttpRequest request, NetworkStream stream, CancellationToken cancellationToken)
    {
        if (request.Method == "OPTIONS")
        {
            await LocalHttpResponse.WriteEmptyAsync(stream, 204, cancellationToken);
            return;
        }

        if (request.Method == "GET" && request.Path == "/health")
        {
            await LocalHttpResponse.WriteJsonAsync(stream, 200, new { ok = true, version = BridgeVersion, assetType = "Image", pixfix = true }, cancellationToken);
            return;
        }

        if (request.Method == "POST" && request.Path == "/upload")
        {
            await HandleUploadAsync(request, stream, cancellationToken);
            return;
        }

        await LocalHttpResponse.WriteJsonAsync(stream, 404, new { error = "Route not found." }, cancellationToken);
    }

    private async Task HandleUploadAsync(LocalHttpRequest request, NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<UploadRequest>(request.Body) ?? throw new InvalidOperationException("The bridge received invalid JSON.");
            ValidateUpload(payload);
            var fileSizeBytes = GetDecodedSize(payload.Data);
            var assetId = await UploadAssetAsync(payload, cancellationToken);
            logStore.Add(new UploadLogEntry(DateTimeOffset.Now, payload.FileName, assetId, payload.CreatorType, payload.CreatorId, payload.ContentType, fileSizeBytes));
            StatusChanged?.Invoke($"Uploaded {payload.FileName} as {assetId}{(payload.Pixfix && string.Equals(payload.ContentType, "image/png", StringComparison.OrdinalIgnoreCase) ? " [Pixfix]" : string.Empty)}");
            await LocalHttpResponse.WriteJsonAsync(stream, 200, new { assetId }, cancellationToken);
        }
        catch (Exception error)
        {
            await LocalHttpResponse.WriteJsonAsync(stream, 400, new { error = error.Message }, cancellationToken);
        }
    }


    private static long GetDecodedSize(string base64Data)
    {
        var padding = base64Data.EndsWith("==", StringComparison.Ordinal) ? 2 : base64Data.EndsWith("=", StringComparison.Ordinal) ? 1 : 0;
        return base64Data.Length * 3L / 4L - padding;
    }

    private static void ValidateUpload(UploadRequest payload)
    {
        if (string.IsNullOrWhiteSpace(payload.ApiKey)) throw new InvalidOperationException("The APIKey is required.");
        if (payload.CreatorType is not ("user" or "group")) throw new InvalidOperationException("Creator type must be user or group.");
        if (!ulong.TryParse(payload.CreatorId, out _)) throw new InvalidOperationException("A valid user/group ID is required.");
        if (string.IsNullOrWhiteSpace(payload.FileName) || string.IsNullOrWhiteSpace(payload.Data)) throw new InvalidOperationException("Image file data is missing.");
    }

    private async Task<string> UploadAssetAsync(UploadRequest payload, CancellationToken cancellationToken)
    {
        var bytes = Convert.FromBase64String(payload.Data);

        if (payload.Pixfix && string.Equals(payload.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            bytes = PixfixProcessor.Apply(bytes);
        }

        using var form = CreateMultipartForm(payload, bytes);
        using var request = new HttpRequestMessage(HttpMethod.Post, RobloxAssetUrl) { Content = form };
        request.Headers.Add("x-api-key", payload.ApiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var result = await ParseRobloxResponseAsync(response, cancellationToken);
        var completedAssetId = GetCompletedAssetId(result);

        if (!string.IsNullOrEmpty(completedAssetId)) return completedAssetId;

        var operationId = GetOperationId(result);
        if (string.IsNullOrEmpty(operationId)) throw new InvalidOperationException("Roblox did not return an upload operation ID.");
        return await PollOperationAsync(operationId, payload.ApiKey, cancellationToken);
    }

    private static MultipartFormDataContent CreateMultipartForm(UploadRequest payload, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(CreateAssetMetadata(payload))), "request");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(payload.ContentType);
        form.Add(fileContent, "fileContent", payload.FileName);
        return form;
    }

    private static object CreateAssetMetadata(UploadRequest payload)
    {
        var creator = payload.CreatorType == "group"
            ? new Dictionary<string, string> { ["groupId"] = payload.CreatorId }
            : new Dictionary<string, string> { ["userId"] = payload.CreatorId };

        return new
        {
            assetType = "Image",
            displayName = GetDisplayName(payload.FileName),
            description = "Uploaded by the RoImport local bridge.",
            creationContext = new { creator }
        };
    }

    private static string GetDisplayName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (string.IsNullOrEmpty(name)) name = "RoImport Image";
        return name[..Math.Min(name.Length, 50)];
    }

    private async Task<JsonElement> ParseRobloxResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = ParseJson(text);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(GetErrorMessage(data, response.StatusCode));
        }

        return data;
    }

    private static JsonElement ParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return JsonDocument.Parse("{}").RootElement.Clone();
        try { return JsonDocument.Parse(text).RootElement.Clone(); }
        catch { return JsonDocument.Parse(JsonSerializer.Serialize(new { message = text })).RootElement.Clone(); }
    }

    private static string GetErrorMessage(JsonElement data, HttpStatusCode statusCode)
    {
        if (TryGetString(data, "message", out var message)) return message;
        if (TryGetString(data, "error", out var error)) return error;
        return $"Roblox returned status {(int)statusCode}.";
    }

    private static string GetOperationId(JsonElement data)
    {
        foreach (var path in new[] { "operationId", "path", "operationPath" })
        {
            if (!TryGetString(data, path, out var value)) continue;
            return value.Contains('/') ? value.Split('/').Last() : value;
        }

        if (!data.TryGetProperty("operation", out var operation)) return string.Empty;
        if (TryGetString(operation, "operationId", out var id)) return id;
        if (TryGetString(operation, "path", out var operationPath)) return operationPath.Split('/').Last();
        return string.Empty;
    }

    private static string GetCompletedAssetId(JsonElement operation)
    {
        if (operation.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.False) return string.Empty;
        if (operation.TryGetProperty("error", out var error)) throw new InvalidOperationException(GetNestedMessage(error));
        return GetAssetId(operation);
    }

    private static string GetAssetId(JsonElement data)
    {
        var paths = new[] { "assetId", "asset.assetId", "response.assetId", "response.asset.assetId", "response.path", "asset.path" };
        foreach (var path in paths)
        {
            var value = GetPathString(data, path);
            var id = ExtractTrailingId(value);
            if (!string.IsNullOrEmpty(id)) return id;
        }

        return string.Empty;
    }

    private async Task<string> PollOperationAsync(string operationId, string apiKey, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{RobloxOperationUrl}/{operationId}");
            request.Headers.Add("x-api-key", apiKey);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var operation = await ParseRobloxResponseAsync(response, cancellationToken);
            var assetId = GetCompletedAssetId(operation);
            if (!string.IsNullOrEmpty(assetId)) return assetId;
            await Task.Delay(1000, cancellationToken);
        }

        throw new InvalidOperationException("Roblox did not finish processing the image in time.");
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var node)) return false;
        value = node.ValueKind == JsonValueKind.String ? node.GetString() ?? string.Empty : node.ToString();
        return !string.IsNullOrEmpty(value);
    }

    private static string GetPathString(JsonElement element, string path)
    {
        var current = element;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current)) return string.Empty;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : current.ToString();
    }

    private static string ExtractTrailingId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var segment = value.Split('/').Last();
        return ulong.TryParse(segment, out _) ? segment : string.Empty;
    }

    private static string GetNestedMessage(JsonElement error)
    {
        return TryGetString(error, "message", out var message) ? message : "Roblox rejected the image upload.";
    }

    public void Dispose()
    {
        cancellationTokenSource.Cancel();
        listener?.Stop();
        httpClient.Dispose();
        cancellationTokenSource.Dispose();
    }
}
