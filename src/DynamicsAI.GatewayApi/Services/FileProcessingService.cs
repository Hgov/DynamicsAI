using System.Text;
using System.Text.Json.Nodes;
using DynamicsAI.GatewayApi.Models;
using MiniExcelLibs;

namespace DynamicsAI.GatewayApi.Services;

public class FileProcessingService
{
    private const int MaxFileSizeBytes  = 20 * 1024 * 1024; // 20 MB
    private const int MaxExcelRows      = 200;
    // ~200k karakter ≈ 50k token — sistem + geçmiş + araçlar için yer bırakır
    private const int MaxTextBlockChars = 200_000;

    public Task<(JsonNode ContentBlock, string Summary)> ProcessAsync(
        FileAttachment file, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(file.Data))
            throw new InvalidOperationException("file.data alanı zorunludur.");
        if (string.IsNullOrWhiteSpace(file.MimeType))
            throw new InvalidOperationException("file.mime_type alanı zorunludur.");

        var bytes = Convert.FromBase64String(file.Data);

        if (bytes.Length > MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"Dosya çok büyük ({bytes.Length / 1024 / 1024} MB). Maksimum boyut 20 MB.");

        var fileName = file.Name ?? "dosya";
        var ext      = Path.GetExtension(fileName).ToLowerInvariant();
        var mimeType = file.MimeType.ToLowerInvariant();

        if (IsImage(mimeType, ext))
        {
            var block = BuildImageBlock(bytes, mimeType);
            return Task.FromResult((block, $"[Resim: {fileName}]"));
        }

        if (mimeType == "application/pdf" || ext == ".pdf")
        {
            var block = BuildDocumentBlock(bytes);
            return Task.FromResult((block, $"[PDF: {fileName}]"));
        }

        if (ext is ".xlsx" or ".xls")
        {
            var block = BuildExcelTextBlock(bytes, fileName);
            return Task.FromResult((block, $"[Excel: {fileName}]"));
        }

        if (ext is ".csv" or ".txt" or ".json" or ".xml" or ".md" or ".log")
        {
            var block = BuildTextBlock(bytes, fileName);
            return Task.FromResult((block, $"[Metin dosyası: {fileName}]"));
        }

        throw new NotSupportedException(
            $"Desteklenmeyen dosya türü: '{ext}'. " +
            "Desteklenen türler: resim (jpg/png/gif/webp), PDF, Excel (xlsx), CSV, TXT, JSON, XML.");
    }

    private static bool IsImage(string mimeType, string ext) =>
        mimeType.StartsWith("image/") ||
        ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";

    private static JsonNode BuildImageBlock(byte[] bytes, string mimeType)
    {
        var mediaType = mimeType switch
        {
            "image/jpg" => "image/jpeg",
            var m when m.StartsWith("image/") => m,
            _ => "image/jpeg"
        };

        return new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"]       = "base64",
                ["media_type"] = mediaType,
                ["data"]       = Convert.ToBase64String(bytes)
            }
        };
    }

    private static JsonNode BuildDocumentBlock(byte[] bytes) =>
        new JsonObject
        {
            ["type"] = "document",
            ["source"] = new JsonObject
            {
                ["type"]       = "base64",
                ["media_type"] = "application/pdf",
                ["data"]       = Convert.ToBase64String(bytes)
            }
        };

    private static JsonNode BuildExcelTextBlock(byte[] bytes, string fileName)
    {
        using var stream = new MemoryStream(bytes);
        var rows = MiniExcel.Query(stream, useHeaderRow: true).Cast<IDictionary<string, object>>().ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Excel dosyası: {fileName} ({rows.Count} satır)");

        if (rows.Count > 0)
        {
            sb.AppendLine(string.Join(",", rows[0].Keys));

            var limit = Math.Min(rows.Count, MaxExcelRows);
            for (var i = 0; i < limit; i++)
                sb.AppendLine(string.Join(",", rows[i].Values.Select(v => Escape(v?.ToString()))));

            if (rows.Count > MaxExcelRows)
                sb.AppendLine($"[... {rows.Count - MaxExcelRows} satır daha gösterilmedi]");
        }

        return new JsonObject { ["type"] = "text", ["text"] = TruncateText(sb.ToString(), fileName) };
    }

    private static JsonNode BuildTextBlock(byte[] bytes, string fileName)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return new JsonObject
        {
            ["type"] = "text",
            ["text"] = TruncateText($"Dosya: {fileName}\n\n{text}", fileName)
        };
    }

    private static string TruncateText(string text, string fileName)
    {
        if (text.Length <= MaxTextBlockChars) return text;
        var dropped = text.Length - MaxTextBlockChars;
        return text[..MaxTextBlockChars]
               + $"\n\n[... {dropped:N0} karakter kırpıldı — dosya içeriği çok büyük: {fileName}]";
    }

    private static string Escape(string? value)
    {
        if (value is null) return "";
        return value.Contains(',') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
