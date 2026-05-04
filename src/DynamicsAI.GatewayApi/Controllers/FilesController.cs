using DynamicsAI.GatewayApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace DynamicsAI.GatewayApi.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController(
    ExportedFileRegistry registry,
    IHttpContextAccessor httpContextAccessor) : ControllerBase
{
    /// <summary>Kayıtlı dosyaları listeler. category: "export" | "upload" | boş = hepsi</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category = null)
    {
        var files = await registry.ListAsync(category);
        var result = files.Select(f => new
        {
            id        = f.Id,
            fileName  = Path.GetFileName(f.FilePath),
            category  = f.Category,
            createdAt = f.CreatedAt,
            exists    = System.IO.File.Exists(f.FilePath),
            url       = BuildUrl(f.Id)
        });
        return Ok(result);
    }

    [HttpGet("{fileId}")]
    public async Task<IActionResult> Download(string fileId)
    {
        var path = await registry.GetFilePathAsync(fileId);
        if (path is null || !System.IO.File.Exists(path))
            return NotFound(new { error = "Dosya bulunamadı veya süresi dolmuş." });

        var mimeType = GetMimeType(path);
        var fileName = Path.GetFileName(path);
        return PhysicalFile(path, mimeType, fileName);
    }

    private string BuildUrl(string fileId)
    {
        var req = httpContextAccessor.HttpContext?.Request;
        return req is null
            ? $"/api/files/{fileId}"
            : $"{req.Scheme}://{req.Host}/api/files/{fileId}";
    }

    private static string GetMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx"         => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pdf"          => "application/pdf",
            ".png"          => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"          => "image/gif",
            ".webp"         => "image/webp",
            ".csv"          => "text/csv",
            ".txt"          => "text/plain",
            ".json"         => "application/json",
            ".xml"          => "application/xml",
            _               => "application/octet-stream"
        };
}
