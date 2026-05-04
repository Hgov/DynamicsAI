using System.Diagnostics;
using System.Text.Json;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;

namespace DynamicsAI.Infrastructure.Dynamics;

public class DynamicsExportService(
    IDynamicsQueryService queryService,
    ILogger<DynamicsExportService> logger) : IDynamicsExportService
{
    public async Task<ExportResult> ExportToExcelAsync(
        TenantConfig tenant,
        ExportRequest request,
        CancellationToken ct = default)
    {
        var outputPath = ResolveOutputPath(request);
        EnsureDirectory(outputPath);

        var sw = Stopwatch.StartNew();
        long totalRecords = 0;

        var limit = request.MaxRecords;

        // MiniExcel sayfa sayfa iter — bellekte tek seferde sadece 1 sayfa (≤5000 kayıt) tutulur.
        // Async HTTP çağrıları Task.Run içinde bloke edilir; bu export için kabul edilebilir.
        IEnumerable<IDictionary<string, object?>> GetRows()
        {
            string? nextUrl = null;
            bool firstPage = true;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                QueryResult page;
                if (firstPage)
                {
                    var queryReq = new QueryRequest
                    {
                        TenantContext    = null,
                        EntityPluralName = request.EntityPluralName,
                        SelectFields     = request.SelectFields,
                        Filter           = request.Filter,
                        OrderBy          = request.OrderBy
                    };
                    page = queryService.ExecuteQueryAsync(tenant, queryReq, ct).GetAwaiter().GetResult();
                    firstPage = false;
                }
                else
                {
                    page = queryService.FetchPageAsync(tenant, nextUrl!, ct).GetAwaiter().GetResult();
                }

                foreach (var record in page.Records)
                {
                    if (limit.HasValue && totalRecords >= limit.Value)
                        yield break;

                    totalRecords++;
                    yield return ConvertRecord(record);
                }

                nextUrl = page.NextLink;

                logger.LogInformation(
                    "Export progress: {Total} kayıt yazıldı, nextLink={HasNext}",
                    totalRecords, !string.IsNullOrEmpty(nextUrl));

                if (string.IsNullOrEmpty(nextUrl))
                    break;

                if (limit.HasValue && totalRecords >= limit.Value)
                    break;
            }
        }

        await Task.Run(() => MiniExcel.SaveAs(outputPath, GetRows(), overwriteFile: true), ct);

        sw.Stop();
        logger.LogInformation(
            "Export tamamlandı: {Total} kayıt, {Path}, {Elapsed:F1}s",
            totalRecords, outputPath, sw.Elapsed.TotalSeconds);

        return new ExportResult
        {
            FilePath       = outputPath,
            TotalRecords   = totalRecords,
            ElapsedSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1)
        };
    }

    private static IDictionary<string, object?> ConvertRecord(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.StartsWith('@')) continue; // OData annotation'larını atla
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String  => prop.Value.GetString(),
                JsonValueKind.True    => true,
                JsonValueKind.False   => false,
                JsonValueKind.Null    => null,
                JsonValueKind.Number  =>
                    prop.Value.TryGetInt64(out var l) ? (object?)l : prop.Value.GetDouble(),
                _                     => prop.Value.ToString()
            };
        }
        return dict;
    }

    private static string ResolveOutputPath(ExportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
            return request.OutputPath;

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(desktop, $"{request.EntityPluralName}_{timestamp}.xlsx");
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
