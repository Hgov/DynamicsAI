using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using DynamicsAI.Application.UseCases;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DynamicsAI.Tests;

public class QueryServiceTests
{
    private readonly Mock<IDynamicsQueryService> _queryService = new();

    private ExecuteQueryUseCase CreateUseCase() =>
        new(_queryService.Object, NullLogger<ExecuteQueryUseCase>.Instance);

    private static TenantContext MakeTenant() => new()
    {
        TenantId = "tenant-1",
        DynamicsUrl = "https://org.crm.dynamics.com",
        ClientId = "client-id",
        ClientSecret = "secret"
    };

    [Fact]
    public async Task ExecuteQuery_CallsServiceWithCorrectTenant()
    {
        var request = new QueryRequest
        {
            TenantContext = MakeTenant(),
            EntityPluralName = "accounts",
            SelectFields = ["name", "telephone1"]
        };
        var expected = new QueryResult { Records = [] };
        _queryService.Setup(s => s.ExecuteQueryAsync(It.IsAny<TenantConfig>(), request, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(expected);

        var result = await CreateUseCase().ExecuteAsync(request);

        Assert.Same(expected, result);
        _queryService.Verify(s => s.ExecuteQueryAsync(
            It.Is<TenantConfig>(t => t.TenantId == "tenant-1"),
            request,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCount_CallsServiceWithCorrectParameters()
    {
        _queryService.Setup(s => s.GetCountAsync(It.IsAny<TenantConfig>(), "accounts", null, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(42);

        var count = await CreateUseCase().GetCountAsync(MakeTenant(), "accounts", null);

        Assert.Equal(42, count);
    }
}
