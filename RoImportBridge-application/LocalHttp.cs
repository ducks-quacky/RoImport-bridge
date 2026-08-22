using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RoImportBridge;

internal sealed class LocalHttpRequest
{
    public string Method { get; private set; } = string.Empty;
    public string Path { get; private set; } = string.Empty;
    public byte[] Body { get; private set; } = Array.Empty<byte>();

    public static async Task<LocalHttpRequest> ReadAsync(NetworkStream stream, int maxBodySize, CancellationToken cancellationToken)
    {
        var headerBytes = await ReadHeadersAsync(stream, cancellationToken);
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2) throw new InvalidOperationException("Invalid HTTP request.");

        var contentLength = GetContentLength(lines);
        if (contentLength > maxBodySize) throw new InvalidOperationException("Image payload is too large.");
        var body = await ReadBodyAsync(stream, contentLength, cancellationToken);

        return new LocalHttpRequest
        {
            Method = requestLine[0].ToUpperInvariant(),
            Path = requestLine[1].Split('?')[0],
            Body = body
        };
    }

    private static async Task<byte[]> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[1];
        var ending = new byte[] { 13, 10, 13, 10 };

        while (memory.Length < 64 * 1024)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            memory.WriteByte(buffer[0]);
            if (EndsWith(memory, ending)) return memory.ToArray();
        }

        throw new InvalidOperationException("Invalid HTTP headers.");
    }

    private static bool EndsWith(MemoryStream memory, byte[] ending)
    {
        if (memory.Length < ending.Length) return false;
        var buffer = memory.GetBuffer();
        var offset = (int)memory.Length - ending.Length;
        for (var index = 0; index < ending.Length; index++) if (buffer[offset + index] != ending[index]) return false;
        return true;
    }

    private static int GetContentLength(string[] lines)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(line.IndexOf(':') + 1)..].Trim();
            if (int.TryParse(value, out var length)) return length;
        }

        return 0;
    }

    private static async Task<byte[]> ReadBodyAsync(NetworkStream stream, int contentLength, CancellationToken cancellationToken)
    {
        if (contentLength <= 0) return Array.Empty<byte>();
        var body = new byte[contentLength];
        var offset = 0;

        while (offset < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset), cancellationToken);
            if (read == 0) throw new InvalidOperationException("The request body ended unexpectedly.");
            offset += read;
        }

        return body;
    }
}

internal static class LocalHttpResponse
{
    public static Task WriteJsonAsync(NetworkStream stream, int statusCode, object body, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        return WriteAsync(stream, statusCode, "application/json; charset=utf-8", bytes, cancellationToken);
    }

    public static Task WriteEmptyAsync(NetworkStream stream, int statusCode, CancellationToken cancellationToken)
    {
        return WriteAsync(stream, statusCode, "text/plain", Array.Empty<byte>(), cancellationToken);
    }

    private static async Task WriteAsync(NetworkStream stream, int statusCode, string contentType, byte[] body, CancellationToken cancellationToken)
    {
        var headers = BuildHeaders(statusCode, contentType, body.Length);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
        if (body.Length > 0) await stream.WriteAsync(body, cancellationToken);
    }

    private static string BuildHeaders(int statusCode, string contentType, int contentLength)
    {
        var reason = statusCode switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            _ => "OK"
        };

        return $"HTTP/1.1 {statusCode} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {contentLength}\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: Content-Type\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
    }
}
