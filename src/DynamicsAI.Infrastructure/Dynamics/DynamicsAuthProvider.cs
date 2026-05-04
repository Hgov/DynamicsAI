using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Domain.Exceptions;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DynamicsAI.Infrastructure.Dynamics;

public class DynamicsAuthProvider(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<DynamicsAuthProvider> logger)
{
    private const string TenantIdCachePrefix = "tenantid:";
    private const string TokenCachePrefix = "token:";

    public async Task<string> AcquireTokenAsync(TenantConfig tenant, CancellationToken ct = default)
    {
        if (tenant.DeploymentType == DeploymentType.OnPrem)
            throw new NotImplementedException("OnPrem authentication will be implemented in FAZ 2 (NTLM/ADFS).");

        var tenantId = !string.IsNullOrWhiteSpace(tenant.TenantId)
            ? tenant.TenantId
            : await DiscoverTenantIdAsync(tenant.DynamicsUrl, ct);

        if (!string.IsNullOrWhiteSpace(tenant.ClientSecret) && !string.IsNullOrWhiteSpace(tenant.ClientId))
            return await AcquireByClientSecretAsync(tenant, tenantId, ct);

        if (!string.IsNullOrWhiteSpace(tenant.Username) && !string.IsNullOrWhiteSpace(tenant.Password))
            return await AcquireByRopcAsync(tenant, tenantId, ct);

        throw new TenantAuthException(tenantId,
            "Kimlik bilgisi eksik — client_secret VEYA username+password sağlanmalıdır.\n" +
            "  Seçenek 1: client_id + client_secret (service principal)\n" +
            "  Seçenek 2: username + password (kullanıcı adı/şifre — MFA olmamalı)");
    }

    // ── Client Credentials (Service Principal) ─────────────────────────────
    private async Task<string> AcquireByClientSecretAsync(TenantConfig tenant, string tenantId, CancellationToken ct)
    {
        var cacheKey = $"{TokenCachePrefix}sp:{tenant.ClientId}:{tenantId}";
        if (memoryCache.TryGetValue(cacheKey, out string? cached) && cached != null)
        {
            logger.LogDebug("Token cache HIT (ClientSecret) for {DynamicsUrl}", tenant.DynamicsUrl);
            return cached;
        }

        logger.LogInformation("Acquiring token via ClientSecret: clientId={ClientId}, tenantId={TenantId}",
            tenant.ClientId, tenantId);

        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var resource = tenant.DynamicsUrl.TrimEnd('/');

        var body = new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = tenant.ClientId,
            ["client_secret"] = tenant.ClientSecret!,
            ["scope"]         = $"{resource}/.default"
        };

        return await PostTokenAsync(tokenUrl, body, cacheKey, "ClientSecret", ct);
    }

    // ── ROPC (Username / Password) ─────────────────────────────────────────
    private async Task<string> AcquireByRopcAsync(TenantConfig tenant, string tenantId, CancellationToken ct)
    {
        var effectiveClientId = string.IsNullOrWhiteSpace(tenant.ClientId)
            ? "51f81489-12ee-4a9e-aaae-a2591f45987d"   // Microsoft'un well-known Dynamics 365 client ID
            : tenant.ClientId;

        var cacheKey = $"{TokenCachePrefix}ropc:{effectiveClientId}:{tenant.Username}:{tenantId}";
        if (memoryCache.TryGetValue(cacheKey, out string? cached) && cached != null)
        {
            logger.LogDebug("Token cache HIT (ROPC) for {Username}", tenant.Username);
            return cached;
        }

        logger.LogInformation(
            "Acquiring token via ROPC: username={Username}, clientId={ClientId} (well-known={IsWellKnown}), tenantId={TenantId}",
            tenant.Username, effectiveClientId,
            effectiveClientId == "51f81489-12ee-4a9e-aaae-a2591f45987d",
            tenantId);

        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var resource = tenant.DynamicsUrl.TrimEnd('/');

        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"]  = effectiveClientId,
            ["username"]   = tenant.Username!,
            ["password"]   = tenant.Password!,
            ["scope"]      = $"{resource}/.default"
        };

        return await PostTokenAsync(tokenUrl, body, cacheKey, "ROPC", ct);
    }

    // ── Ortak HTTP token isteği ─────────────────────────────────────────────
    private async Task<string> PostTokenAsync(
        string tokenUrl,
        Dictionary<string, string> body,
        string cacheKey,
        string method,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("DynamicsToken");
        using var req = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(body)
        };

        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Token request failed ({Method}) — HTTP {Status}: {Body}", method, (int)resp.StatusCode, raw);

            var hint = BuildHint(raw);
            throw new TenantAuthException("unknown",
                $"Token alınamadı ({method}) — HTTP {(int)resp.StatusCode}:\n{raw}{hint}");
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var tokenEl))
            throw new TenantAuthException("unknown", $"Token yanıtında 'access_token' bulunamadı: {raw}");

        var token = tokenEl.GetString()!;

        // expires_in saniye cinsinden; 5 dk tampon bırak
        var ttl = root.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var exp)
            ? TimeSpan.FromSeconds(Math.Max(exp - 300, 60))
            : TimeSpan.FromMinutes(55);

        memoryCache.Set(cacheKey, token, ttl);
        logger.LogInformation("Token acquired ({Method}), expires in {Ttl}", method, ttl);
        return token;
    }

    // ── Tenant ID keşfi ────────────────────────────────────────────────────
    private async Task<string> DiscoverTenantIdAsync(string dynamicsUrl, CancellationToken ct)
    {
        var cacheKey = $"{TenantIdCachePrefix}{dynamicsUrl}";
        if (memoryCache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        logger.LogInformation("Discovering tenant ID from {DynamicsUrl}", dynamicsUrl);

        var client = httpClientFactory.CreateClient("DynamicsDiscovery");
        using var resp = await client.GetAsync($"{dynamicsUrl.TrimEnd('/')}/api/data/v9.2/", ct);
        var wwwAuth = resp.Headers.WwwAuthenticate.ToString();

        logger.LogDebug("WWW-Authenticate header: {Header}", wwwAuth);

        var tenantId = ParseTenantId(wwwAuth);
        if (tenantId is null)
            throw new TenantAuthException("unknown",
                $"Tenant ID keşfedilemedi.\n" +
                $"URL: {dynamicsUrl}\n" +
                $"WWW-Authenticate: {wwwAuth}\n" +
                "Çözüm: tenant_context içinde tenant_id GUID'ini açıkça sağlayın.");

        logger.LogInformation("Discovered tenant ID {TenantId} for {DynamicsUrl}", tenantId, dynamicsUrl);
        memoryCache.Set(cacheKey, tenantId, TimeSpan.FromHours(24));
        return tenantId;
    }

    private static string? ParseTenantId(string wwwAuthenticate)
    {
        var match = Regex.Match(
            wwwAuthenticate,
            @"authorization_uri=""?https://login\.microsoftonline\.com/([^/""\s]+)/",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string BuildHint(string errorBody)
    {
        if (errorBody.Contains("AADSTS50076") || errorBody.Contains("MFA") || errorBody.Contains("multi-factor"))
            return "\n\n⚠ ÇÖZÜM: Bu hesapta MFA aktif. ROPC (şifre) akışı MFA ile çalışmaz.\n" +
                   "  Seçenek 1: Azure Portal'dan bu hesap için MFA'yı devre dışı bırakın.\n" +
                   "  Seçenek 2: client_id + client_secret (service principal) kullanın.";

        if (errorBody.Contains("AADSTS70011") || errorBody.Contains("invalid_scope"))
            return "\n\n⚠ ÇÖZÜM: Scope geçersiz. Dynamics URL'ini kontrol edin.";

        if (errorBody.Contains("AADSTS700016") || errorBody.Contains("application") && errorBody.Contains("not found"))
            return "\n\n⚠ ÇÖZÜM: client_id Azure AD'de bulunamadı. Azure Portal > App Registrations'dan doğru client_id'yi alın.";

        if (errorBody.Contains("AADSTS50034") || errorBody.Contains("user does not exist"))
            return "\n\n⚠ ÇÖZÜM: Kullanıcı bulunamadı. username (e-posta) adresini kontrol edin.";

        if (errorBody.Contains("AADSTS50126") || errorBody.Contains("Invalid credentials") || errorBody.Contains("wrong password"))
            return "\n\n⚠ ÇÖZÜM: Şifre hatalı. Password alanını kontrol edin.";

        if (errorBody.Contains("AADSTS65001") || errorBody.Contains("consent"))
            return "\n\n⚠ ÇÖZÜM: Uygulama Dynamics'e erişim izni almamış.\n" +
                   "  Azure Portal > App Registrations > API Permissions > Dynamics CRM > grant admin consent.";

        return string.Empty;
    }
}
