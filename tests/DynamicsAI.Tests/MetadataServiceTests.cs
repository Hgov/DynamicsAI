using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using DynamicsAI.Application.UseCases;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DynamicsAI.Tests;

public class MetadataServiceTests
{
    private readonly Mock<IDynamicsMetadataService> _metadataService = new();
    private readonly Mock<ICacheService> _cache = new();

    private GetMetadataUseCase CreateUseCase() =>
        new(_metadataService.Object, _cache.Object, NullLogger<GetMetadataUseCase>.Instance);

    private static TenantContext MakeTenant(string id = "tenant-1") => new()
    {
        TenantId = id,
        DynamicsUrl = "https://org.crm.dynamics.com",
        ClientId = "client-id",
        ClientSecret = "secret"
    };

    [Fact]
    public async Task GetMetadata_CacheHit_ReturnsFromCacheWithoutCallingService()
    {
        var cached = new EntityMetadata { Entities = [], FromCache = true };
        _cache.Setup(c => c.GetAsync<EntityMetadata>($"metadata:tenant-1"))
              .ReturnsAsync(cached);

        var result = await CreateUseCase().ExecuteAsync(MakeTenant(), forceRefresh: false);

        Assert.True(result.FromCache);
        _metadataService.Verify(s => s.GetEntitySchemasAsync(It.IsAny<TenantConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMetadata_CacheMiss_CallsServiceAndSetsCache()
    {
        var schemas = new List<EntitySchema>
        {
            new() { LogicalName = "account", DisplayName = "Account", PluralName = "accounts" }
        };
        _cache.Setup(c => c.GetAsync<EntityMetadata>(It.IsAny<string>())).ReturnsAsync((EntityMetadata?)null);
        _metadataService.Setup(s => s.GetEntitySchemasAsync(It.IsAny<TenantConfig>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(schemas);

        var result = await CreateUseCase().ExecuteAsync(MakeTenant(), forceRefresh: false);

        Assert.Single(result.Entities);
        _cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<EntityMetadata>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task GetMetadata_ForceRefresh_BypassesCache()
    {
        var cached = new EntityMetadata { Entities = [], FromCache = true };
        _cache.Setup(c => c.GetAsync<EntityMetadata>(It.IsAny<string>())).ReturnsAsync(cached);
        _metadataService.Setup(s => s.GetEntitySchemasAsync(It.IsAny<TenantConfig>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync([]);

        await CreateUseCase().ExecuteAsync(MakeTenant(), forceRefresh: true);

        _metadataService.Verify(s => s.GetEntitySchemasAsync(It.IsAny<TenantConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
