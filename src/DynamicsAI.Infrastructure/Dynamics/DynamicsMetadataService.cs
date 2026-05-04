using System.Net.Http.Headers;
using System.Text.Json;
using DynamicsAI.Application.Interfaces;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DynamicsAI.Infrastructure.Dynamics;

public class DynamicsMetadataService(
    DynamicsAuthProvider authProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<DynamicsMetadataService> logger) : IDynamicsMetadataService
{
    private const string ApiVersion = "v9.2";
    private const int AttributeFetchConcurrency = 10;

    public async Task<IReadOnlyList<EntitySchema>> GetEntitySchemasAsync(TenantConfig tenant, CancellationToken ct = default)
    {
        var token = await authProvider.AcquireTokenAsync(tenant, ct);
        var client = CreateClient(token);

        // Step 1: entity listesini al ($expand olmadan — daha hızlı ve güvenilir)
        var entityInfos = await FetchEntityListAsync(client, tenant, ct);
        logger.LogInformation("Found {Count} entities, fetching attributes in parallel...", entityInfos.Count);

        // Step 2: her entity için attributes'ı ayrı endpoint'ten al (parallel)
        var semaphore = new SemaphoreSlim(AttributeFetchConcurrency, AttributeFetchConcurrency);

        var schemaTasks = entityInfos.Select(async info =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var fields = await FetchEntityAttributesAsync(client, tenant, info.LogicalName, ct);
                logger.LogDebug("Entity {LogicalName}: {FieldCount} fields", info.LogicalName, fields.Count);
                return new EntitySchema
                {
                    LogicalName = info.LogicalName,
                    DisplayName = info.DisplayName,
                    PluralName = info.EntitySetName,
                    Fields = fields
                };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var schemas = await Task.WhenAll(schemaTasks);
        logger.LogInformation("Fetched schemas for {Count} entities", schemas.Length);
        return schemas;
    }

    private async Task<List<(string LogicalName, string EntitySetName, string DisplayName)>> FetchEntityListAsync(
        HttpClient client, TenantConfig tenant, CancellationToken ct)
    {
        var entities = new List<(string, string, string)>();
        string? nextLink = $"{tenant.DynamicsUrl.TrimEnd('/')}/api/data/{ApiVersion}/EntityDefinitions" +
                           "?$select=LogicalName,EntitySetName,DisplayName";

        logger.LogInformation("Fetching entity list from Dynamics");

        while (nextLink is not null)
        {
            using var response = await client.GetAsync(nextLink, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("EntityDefinitions failed {Status}: {Body}", response.StatusCode, err);
                throw new InvalidOperationException($"Metadata sorgusu başarısız ({(int)response.StatusCode}): {err}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueEl))
            {
                foreach (var entity in valueEl.EnumerateArray())
                {
                    var logicalName = entity.TryGetProperty("LogicalName", out var ln) ? ln.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(logicalName)) continue;

                    var entitySetName = entity.TryGetProperty("EntitySetName", out var esn)
                        ? esn.GetString() ?? logicalName
                        : logicalName;

                    var displayName = ExtractLabel(entity, "DisplayName") ?? logicalName;
                    entities.Add((logicalName, entitySetName, displayName));
                }
            }

            nextLink = root.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
        }

        return entities;
    }

    private async Task<List<FieldSchema>> FetchEntityAttributesAsync(
        HttpClient client, TenantConfig tenant, string logicalName, CancellationToken ct)
    {
        var fields = new List<FieldSchema>();
        string? nextLink = $"{tenant.DynamicsUrl.TrimEnd('/')}/api/data/{ApiVersion}" +
                           $"/EntityDefinitions(LogicalName='{logicalName}')/Attributes" +
                           "?$select=LogicalName,DisplayName,AttributeType,RequiredLevel";

        while (nextLink is not null)
        {
            using var response = await client.GetAsync(nextLink, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Attributes fetch failed for {Entity} ({Status}): {Body}",
                    logicalName, response.StatusCode, err);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueEl))
                ParseAttributes(valueEl, fields);

            nextLink = root.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
        }

        return fields;
    }

    private static void ParseAttributes(JsonElement attrArray, List<FieldSchema> fields)
    {
        foreach (var attr in attrArray.EnumerateArray())
        {
            var attrLogical = attr.TryGetProperty("LogicalName", out var aln) ? aln.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(attrLogical)) continue;

            var attrDisplay = ExtractLabel(attr, "DisplayName") ?? attrLogical;
            var attrType = attr.TryGetProperty("AttributeType", out var at) ? at.GetString() ?? "Unknown" : "Unknown";

            var isRequired = false;
            if (attr.TryGetProperty("RequiredLevel", out var rl) && rl.ValueKind != JsonValueKind.Null)
                if (rl.TryGetProperty("Value", out var rlv))
                    isRequired = rlv.GetString() is "ApplicationRequired" or "SystemRequired";

            fields.Add(new FieldSchema
            {
                LogicalName = attrLogical,
                DisplayName = attrDisplay,
                FieldType = attrType,
                IsRequired = isRequired
            });
        }
    }

    private static string? ExtractLabel(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;

        if (prop.TryGetProperty("UserLocalizedLabel", out var ull) && ull.ValueKind != JsonValueKind.Null)
            if (ull.TryGetProperty("Label", out var label))
                return label.GetString();

        if (prop.TryGetProperty("LocalizedLabels", out var labels) && labels.ValueKind == JsonValueKind.Array)
            foreach (var l in labels.EnumerateArray())
                if (l.TryGetProperty("Label", out var lbl))
                    return lbl.GetString();

        return null;
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
