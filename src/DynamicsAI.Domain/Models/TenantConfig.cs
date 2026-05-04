namespace DynamicsAI.Domain.Models;

public class TenantConfig
{
    public string? TenantId { get; init; }
    public required string DynamicsUrl { get; init; }
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public DeploymentType DeploymentType { get; init; } = DeploymentType.Online;
}

public enum DeploymentType
{
    Online,
    OnPrem
}
