using System.Text.Json;
using DynamicsAI.Infrastructure.Cache;
using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace DynamicsAI.Tests;

public class DistributedCacheServiceTests
{
    private readonly Mock<IDistributedCache> _cache = new();

    private DistributedCacheService CreateService() => new(_cache.Object);

    [Fact]
    public async Task GetAsync_KeyExists_DeserializesValue()
    {
        var obj = new Payload { Name = "Test", Value = 42 };
        _cache.Setup(c => c.GetAsync("key", It.IsAny<CancellationToken>()))
              .ReturnsAsync(JsonSerializer.SerializeToUtf8Bytes(obj));

        var result = await CreateService().GetAsync<Payload>("key");

        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_KeyMissing_ReturnsNull()
    {
        _cache.Setup(c => c.GetAsync("missing", It.IsAny<CancellationToken>()))
              .ReturnsAsync((byte[]?)null);

        var result = await CreateService().GetAsync<Payload>("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_StoresSerializedBytes_WithCorrectTtl()
    {
        await CreateService().SetAsync("key", new Payload { Name = "X" }, TimeSpan.FromMinutes(30));

        _cache.Verify(c => c.SetAsync(
            "key",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(30)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_CallsCacheRemove()
    {
        await CreateService().RemoveAsync("key");

        _cache.Verify(c => c.RemoveAsync("key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RoundTrip_SetThenGet_ReturnsOriginalValue()
    {
        byte[]? stored = null;
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
              .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, b, _, _) => stored = b)
              .Returns(Task.CompletedTask);
        _cache.Setup(c => c.GetAsync("k", It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => stored);

        var service = CreateService();
        await service.SetAsync("k", new Payload { Name = "RoundTrip", Value = 99 }, TimeSpan.FromMinutes(1));
        var result = await service.GetAsync<Payload>("k");

        Assert.NotNull(result);
        Assert.Equal("RoundTrip", result.Name);
        Assert.Equal(99, result.Value);
    }

    private class Payload
    {
        public string Name { get; init; } = "";
        public int Value { get; init; }
    }
}
