using System.Text.Json.Serialization;
using DynamicsAI.Application.DTOs;

namespace DynamicsAI.GatewayApi.Models;

public class ChatRequest
{
    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = "default";

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("tenant_context")]
    public TenantContext? TenantContext { get; init; }

    [JsonPropertyName("anthropic_api_key")]
    public string? AnthropicApiKey { get; init; }

    /// <summary>
    /// Model override. Örnek: "claude-sonnet-4-6", "claude-opus-4-7"
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// İsteğe bağlı dosya eki. Göndermek zorunda değilsiniz — sadece dosya analizi gerektiğinde ekleyin.
    /// Desteklenen türler: resim (jpg/png/gif/webp), PDF, Excel (.xlsx), CSV, TXT, JSON, XML.
    /// </summary>
    [JsonPropertyName("file")]
    public FileAttachment? File { get; init; }
}

public class FileAttachment
{
    /// <summary>Dosya adı, ör. "rapor.xlsx"</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Dosyanın base64 encode edilmiş içeriği</summary>
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>MIME tipi, ör. "image/png", "application/pdf", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"</summary>
    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }
}
