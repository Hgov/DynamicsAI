using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DynamicsAI.Infrastructure.Dynamics;

public class DynamicsCrudService(
    DynamicsAuthProvider authProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<DynamicsCrudService> logger) : IDynamicsCrudService
{
    private const string ApiVersion = "v9.2";

    public async Task<CrudResult> ExecuteAsync(TenantConfig tenant, CrudRequest request, CancellationToken ct = default)
    {
        var token = await authProvider.AcquireTokenAsync(tenant, ct);
        var client = CreateClient(token);
        var baseUrl = $"{tenant.DynamicsUrl.TrimEnd('/')}/api/data/{ApiVersion}/{request.EntityPluralName}";

        return request.Operation switch
        {
            CrudOperation.Create => await CreateAsync(client, baseUrl, request, ct),
            CrudOperation.Update => await UpdateAsync(client, baseUrl, request, ct),
            CrudOperation.Delete => await DeleteAsync(client, baseUrl, request, ct),
            _ => throw new ArgumentException($"Bilinmeyen operasyon: {request.Operation}")
        };
    }

    private async Task<CrudResult> CreateAsync(HttpClient client, string baseUrl, CrudRequest request, CancellationToken ct)
    {
        var payload = request.Payload?.GetRawText() ?? "{}";
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        logger.LogInformation("OData POST {Url}", baseUrl);

        using var response = await client.PostAsync(baseUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Create failed {Status}: {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Kayıt oluşturma başarısız ({(int)response.StatusCode}): {body}");
        }

        var entityId = response.Headers.TryGetValues("OData-EntityId", out var values)
            ? ExtractGuidFromEntityIdHeader(values.First())
            : null;

        logger.LogInformation("Created {Entity} with ID {Id}", request.EntityPluralName, entityId);
        return CrudResult.Created(entityId ?? "unknown");
    }

    private async Task<CrudResult> UpdateAsync(HttpClient client, string baseUrl, CrudRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RecordId))
            throw new ArgumentException("Update operasyonu için record_id zorunludur.");

        var url = $"{baseUrl}({request.RecordId})";
        var payload = request.Payload?.GetRawText() ?? "{}";
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };

        logger.LogInformation("OData PATCH {Url}", url);

        using var response = await client.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Update failed {Status}: {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Kayıt güncelleme başarısız ({(int)response.StatusCode}): {body}");
        }

        logger.LogInformation("Updated {Entity} {Id}", request.EntityPluralName, request.RecordId);
        return CrudResult.Updated(request.RecordId);
    }

    private async Task<CrudResult> DeleteAsync(HttpClient client, string baseUrl, CrudRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RecordId))
            throw new ArgumentException("Delete operasyonu için record_id zorunludur.");

        var url = $"{baseUrl}({request.RecordId})";

        logger.LogInformation("OData DELETE {Url}", url);

        using var response = await client.DeleteAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Delete failed {Status}: {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Kayıt silme başarısız ({(int)response.StatusCode}): {body}");
        }

        logger.LogInformation("Deleted {Entity} {Id}", request.EntityPluralName, request.RecordId);
        return CrudResult.Deleted(request.RecordId!);
    }

    private static string? ExtractGuidFromEntityIdHeader(string entityIdUrl)
    {
        var match = Regex.Match(
            entityIdUrl,
            @"\(([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private HttpClient CreateClient(string token)
    {
        var client = httpClientFactory.CreateClient("DynamicsApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        return client;
    }
}
