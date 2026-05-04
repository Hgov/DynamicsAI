using DynamicsAI.GatewayApi.Models;
using DynamicsAI.GatewayApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace DynamicsAI.GatewayApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(
    ClaudeAgentService agentService,
    ConversationService conversationService,
    FileProcessingService fileProcessor,
    ExportedFileRegistry fileRegistry,
    StorageOptions storageOptions,
    ILogger<ChatController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken ct)
    {
        System.Text.Json.Nodes.JsonNode? fileBlock = null;
        string? fileSummary = null;

        if (request.File is not null && !string.IsNullOrWhiteSpace(request.File.Data))
        {
            try
            {
                (fileBlock, fileSummary) = await fileProcessor.ProcessAsync(request.File, ct);

                // Dosyayı uploads/ klasörüne kalıcı olarak kaydet
                var bytes    = Convert.FromBase64String(request.File.Data);
                var ext      = Path.GetExtension(request.File.Name ?? "file");
                var savePath = Path.Combine(storageOptions.UploadsPath, $"{Guid.NewGuid():N}{ext}");
                await System.IO.File.WriteAllBytesAsync(savePath, bytes, ct);

                var fileId  = await fileRegistry.RegisterAsync(savePath, "upload");
                var fileUrl = BuildDownloadUrl(fileId);

                // Session geçmişinde tıklanabilir link olarak sakla
                var displayName = request.File.Name ?? "dosya";
                fileSummary = $"[{displayName}]({fileUrl})";
            }
            catch (NotSupportedException ex)     { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
            catch (FormatException)              { return BadRequest(new { error = "file.data geçerli base64 değil." }); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dosya kaydedilemedi");
                return StatusCode(500, new { error = $"Dosya işleme hatası: {ex.Message}" });
            }
        }

        try
        {
            var (sessionId, message, toolCallsMade) = await agentService.SendMessageAsync(
                request.SessionId,
                request.UserId,
                request.Message,
                request.TenantContext,
                request.AnthropicApiKey,
                request.Model,
                fileBlock,
                fileSummary,
                ct: ct);

            return Ok(new ChatResponse { SessionId = sessionId, Message = message, ToolCallsMade = toolCallsMade });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat isteği başarısız");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetUserSessions([FromQuery] string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId zorunludur." });

        var sessions = await conversationService.GetUserSessionsAsync(userId);
        return Ok(sessions);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSessionMessages(string sessionId)
    {
        var messages = await conversationService.GetSessionMessagesAsync(sessionId);
        return Ok(messages);
    }

    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> DeleteSession(string sessionId)
    {
        await conversationService.DeleteAsync(sessionId);
        return NoContent();
    }

    [HttpGet("health")]
    public IActionResult Health() =>
        Ok(new
        {
            status    = "ok",
            sessions  = conversationService.ActiveSessionCount,
            timestamp = DateTime.UtcNow
        });

    private string BuildDownloadUrl(string fileId)
    {
        var req = HttpContext.Request;
        return $"{req.Scheme}://{req.Host}/api/files/{fileId}";
    }
}
