using System.Net.Http.Headers;
using System.Text.Json;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DynamicsAI.Infrastructure.Dynamics;

public class DynamicsQueryService(
    DynamicsAuthProvider authProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<DynamicsQueryService> logger) : IDynamicsQueryService
{
    private const string ApiVersion = "v9.2";

    public async Task<QueryResult> ExecuteQueryAsync(TenantConfig tenant, QueryRequest request, CancellationToken ct = default)
    {
        var token = await authProvider.AcquireTokenAsync(tenant, ct);
        var url = BuildQueryUrl(tenant.DynamicsUrl, request);
        logger.LogInformation("OData GET {Url}", url);
        var result = await FetchUrlAsync(token, url, ct);
        logger.LogInformation("Query returned {Count} record(s) for {Entity}", result.Records.Count, request.EntityPluralName);
        return result;
    }

    public async Task<QueryResult> FetchPageAsync(TenantConfig tenant, string pageUrl, CancellationToken ct = default)
    {
        var token = await authProvider.AcquireTokenAsync(tenant, ct);
        logger.LogInformation("OData paginated GET {Url}", pageUrl);
        return await FetchUrlAsync(token, pageUrl, ct);
    }

    private async Task<QueryResult> FetchUrlAsync(string token, string url, CancellationToken ct)
    {
        var client = CreateClient(token);
        using var response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Dynamics query failed {Status}: {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Dynamics sorgusu başarısız ({(int)response.StatusCode}): {body}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var records = root.TryGetProperty("value", out var valueEl)
            ? valueEl.EnumerateArray().Select(e => e.Clone()).ToList()
            : [];

        int? totalCount = null;
        if (root.TryGetProperty("@odata.count", out var countEl) && countEl.TryGetInt32(out var c))
            totalCount = c;

        string? nextLink = null;
        if (root.TryGetProperty("@odata.nextLink", out var nextEl))
            nextLink = nextEl.GetString();

        return new QueryResult { Records = records, TotalCount = totalCount, NextLink = nextLink };
    }

    public async Task<int> GetCountAsync(TenantConfig tenant, string entityPluralName, string? filter, CancellationToken ct = default)
    {
        var token = await authProvider.AcquireTokenAsync(tenant, ct);
        var baseUrl = $"{tenant.DynamicsUrl.TrimEnd('/')}/api/data/{ApiVersion}/{entityPluralName}/$count";
        if (!string.IsNullOrWhiteSpace(filter))
            baseUrl += $"?$filter={Uri.EscapeDataString(filter)}";

        logger.LogInformation("OData $count GET {Url}", baseUrl);

        var client = CreateClient(token);
        using var response = await client.GetAsync(baseUrl, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Count sorgusu başarısız ({(int)response.StatusCode}): {body}");
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        return int.TryParse(text.Trim(), out var count) ? count : 0;
    }

    private static string BuildQueryUrl(string dynamicsUrl, QueryRequest request)
    {
        var parts = new List<string>();

        if (request.SelectFields.Count > 0)
            parts.Add($"$select={string.Join(",", request.SelectFields)}");

        if (!string.IsNullOrWhiteSpace(request.Filter))
            parts.Add($"$filter={Uri.EscapeDataString(request.Filter)}");

        if (!string.IsNullOrWhiteSpace(request.OrderBy))
            parts.Add($"$orderby={Uri.EscapeDataString(request.OrderBy)}");

        if (request.Top.HasValue && request.Top > 0)
            parts.Add($"$top={request.Top}");

        if (request.Count)
            parts.Add("$count=true");

        var query = parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
        return $"{dynamicsUrl.TrimEnd('/')}/api/data/{ApiVersion}/{request.EntityPluralName}{query}";
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
