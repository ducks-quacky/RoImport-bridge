using System.Text.Json.Serialization;

namespace RoImportBridge;

internal sealed class UploadRequest
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("creatorType")]
    public string CreatorType { get; set; } = string.Empty;

    [JsonPropertyName("creatorId")]
    public string CreatorId { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "image/png";
}

internal sealed record UploadLogEntry(
    DateTimeOffset UploadedAt,
    string FileName,
    string AssetId,
    string CreatorType,
    string CreatorId,
    string ContentType,
    long FileSizeBytes
);
