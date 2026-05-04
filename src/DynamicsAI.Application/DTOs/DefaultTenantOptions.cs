using DynamicsAI.Domain.Models;

namespace DynamicsAI.Application.DTOs;

public class DefaultTenantOptions
{
    public const string SectionName = "DynamicsAI:DefaultTenant";

    public string DynamicsUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? TenantId { get; set; }
    public DeploymentType DeploymentType { get; set; } = DeploymentType.Online;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(DynamicsUrl);

    public TenantContext ToTenantContext() => new()
    {
        DynamicsUrl = DynamicsUrl,
        ClientId = ClientId,
        ClientSecret = ClientSecret,
        Username = Username,
        Password = Password,
        TenantId = TenantId,
        DeploymentType = DeploymentType
    };
}
