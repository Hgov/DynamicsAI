namespace DynamicsAI.Domain.Exceptions;

public class TenantAuthException : Exception
{
    public string TenantId { get; }

    public TenantAuthException(string tenantId, string message) : base(message)
    {
        TenantId = tenantId;
    }

    public TenantAuthException(string tenantId, string message, Exception inner) : base(message, inner)
    {
        TenantId = tenantId;
    }
}
