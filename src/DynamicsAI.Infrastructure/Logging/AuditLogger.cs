using Microsoft.Extensions.Logging;

namespace DynamicsAI.Infrastructure.Logging;

public class AuditLogger(ILogger<AuditLogger> logger)
{
    public void LogToolCall(string tenantId, string toolName, long durationMs, bool success)
    {
        logger.LogInformation(
            "[AUDIT] Tenant={TenantId} Tool={Tool} Duration={DurationMs}ms Success={Success}",
            tenantId, toolName, durationMs, success);
    }
}
