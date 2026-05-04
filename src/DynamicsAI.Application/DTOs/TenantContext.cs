using System.ComponentModel;
using System.Text.Json.Serialization;
using DynamicsAI.Domain.Models;

namespace DynamicsAI.Application.DTOs;

public class TenantContext
{
    [Description("Azure AD Tenant ID (GUID). Online ortamlarda otomatik keşfedilir, boş bırakabilirsiniz. OnPrem için gerekli değil.")]
    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [Description("Dynamics 365 URL. Örnek: https://orgname.crm.dynamics.com veya http://crm-server/orgname")]
    [JsonPropertyName("dynamics_url")]
    public required string DynamicsUrl { get; init; }

    [Description("Azure AD uygulama (app registration) Client ID. Service principal veya ROPC akışı için gerekli.")]
    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [Description("Service principal kimlik doğrulama için Client Secret. Username/password kullanıyorsanız boş bırakın.")]
    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; init; }

    [Description("Kullanıcı e-posta adresi. client_secret yerine kullanıcı adı/şifre ile giriş için kullanın.")]
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [Description("Kullanıcı şifresi. client_secret yerine kullanıcı adı/şifre ile giriş için kullanın. MFA aktifse çalışmaz.")]
    [JsonPropertyName("password")]
    public string? Password { get; init; }

    [Description("Online (varsayılan) veya OnPrem.")]
    [JsonPropertyName("deployment_type")]
    public DeploymentType DeploymentType { get; init; } = DeploymentType.Online;

    public AuthMethod GetAuthMethod()
    {
        if (!string.IsNullOrWhiteSpace(ClientSecret)) return AuthMethod.ClientSecret;
        if (!string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password)) return AuthMethod.UsernamePassword;
        throw new InvalidOperationException(
            "Kimlik bilgisi eksik: client_secret VEYA username+password sağlanmalıdır.");
    }

    public TenantConfig ToTenantConfig() => new()
    {
        TenantId = TenantId,
        DynamicsUrl = DynamicsUrl.TrimEnd('/'),
        ClientId = ClientId,
        ClientSecret = ClientSecret,
        Username = Username,
        Password = Password,
        DeploymentType = DeploymentType
    };

    public override string ToString() =>
        $"TenantContext {{ TenantId={TenantId}, DynamicsUrl={DynamicsUrl}, " +
        $"ClientId={ClientId}, AuthMethod={GetAuthMethod()}, DeploymentType={DeploymentType} }}";
}

public enum AuthMethod { ClientSecret, UsernamePassword }
