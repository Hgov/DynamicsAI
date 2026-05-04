using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using DynamicsAI.Application.UseCases;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DynamicsAI.Tests;

public class CrudServiceTests
{
    private readonly Mock<IDynamicsCrudService> _crudService = new();

    private ExecuteCrudUseCase CreateUseCase() =>
        new(_crudService.Object, NullLogger<ExecuteCrudUseCase>.Instance);

    private static TenantContext MakeTenant() => new()
    {
        TenantId = "tenant-1",
        DynamicsUrl = "https://org.crm.dynamics.com",
        ClientId = "client-id",
        ClientSecret = "secret"
    };

    [Fact]
    public async Task ExecuteCrud_Create_CallsServiceAndReturnsResult()
    {
        var request = new CrudRequest
        {
            TenantContext = MakeTenant(),
            Operation = CrudOperation.Create,
            EntityPluralName = "accounts"
        };
        var expected = CrudResult.Created("new-guid-123");
        _crudService.Setup(s => s.ExecuteAsync(It.IsAny<TenantConfig>(), request, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expected);

        var result = await CreateUseCase().ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.Equal("new-guid-123", result.RecordId);
    }

    [Fact]
    public async Task ExecuteCrud_Delete_PassesCorrectTenantConfig()
    {
        var request = new CrudRequest
        {
            TenantContext = MakeTenant(),
            Operation = CrudOperation.Delete,
            EntityPluralName = "accounts",
            RecordId = "some-guid"
        };
        _crudService.Setup(s => s.ExecuteAsync(It.IsAny<TenantConfig>(), request, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(CrudResult.Deleted("some-guid"));

        await CreateUseCase().ExecuteAsync(request);

        _crudService.Verify(s => s.ExecuteAsync(
            It.Is<TenantConfig>(t => t.TenantId == "tenant-1" && t.ClientSecret == "secret"),
            request,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
