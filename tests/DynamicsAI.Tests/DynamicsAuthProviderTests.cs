using System.Net;
using System.Net.Http.Headers;
using DynamicsAI.Domain.Exceptions;
using DynamicsAI.Domain.Models;
using DynamicsAI.Infrastructure.Dynamics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace DynamicsAI.Tests;

public class DynamicsAuthProviderTests
{
    private const string DynamicsUrl = "https://org.crm.dynamics.com";
    private const string ExpectedTenantId = "abc12345-0000-0000-0000-000000000000";

    private static TenantConfig MakeClientSecretTenant(string? tenantId = null) => new()
    {
        TenantId = tenantId,
        DynamicsUrl = DynamicsUrl,
        ClientId = "client-id",
        ClientSecret = "secret",
        DeploymentType = DeploymentType.Online
    };

    private static TenantConfig MakeUserPasswordTenant(string? tenantId = null) => new()
    {
        TenantId = tenantId,
        DynamicsUrl = DynamicsUrl,
        ClientId = "client-id",
        Username = "user@example.com",
        Password = "pass123",
        DeploymentType = DeploymentType.Online
    };

    private static IMemoryCache CreateCache() =>
        new MemoryCache(new MemoryCacheOptions());

    private static IHttpClientFactory CreateHttpFactory(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var client = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("DynamicsDiscovery")).Returns(client);
        return factory.Object;
    }

    private static HttpResponseMessage Make401WithTenantId(string tenantId)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Add(
            new AuthenticationHeaderValue("Bearer",
                $"authorization_uri=\"https://login.microsoftonline.com/{tenantId}/oauth2/authorize\", " +
                $"resource_id=\"{DynamicsUrl}/\""));
        return response;
    }

    [Fact]
    public async Task DiscoverTenantId_ParsedCorrectly_From401Header()
    {
        var factory = CreateHttpFactory(Make401WithTenantId(ExpectedTenantId));
        var cache = CreateCache();
        var provider = new DynamicsAuthProvider(factory, cache, NullLogger<DynamicsAuthProvider>.Instance);

        // MSAL gerçek ağa gider ve başarısız olur — ama öncesinde tenant discovery cache'e yazmalı
        await Assert.ThrowsAnyAsync<Exception>(() =>
            provider.AcquireTokenAsync(MakeClientSecretTenant(tenantId: null)));

        Assert.True(cache.TryGetValue($"tenantid:{DynamicsUrl}", out string? cached));
        Assert.Equal(ExpectedTenantId, cached);
    }

    [Fact]
    public async Task AcquireToken_WithExplicitTenantId_SkipsDiscovery()
    {
        var factory = new Mock<IHttpClientFactory>();
        var cache = CreateCache();
        var provider = new DynamicsAuthProvider(factory.Object, cache, NullLogger<DynamicsAuthProvider>.Instance);

        // Explicit tenant_id verildiğinde discovery HTTP çağrısı yapılmaz
        await Assert.ThrowsAnyAsync<Exception>(() =>
            provider.AcquireTokenAsync(MakeClientSecretTenant(tenantId: ExpectedTenantId)));

        factory.Verify(f => f.CreateClient("DynamicsDiscovery"), Times.Never);
    }

    [Fact]
    public async Task AcquireToken_UsernamePassword_WithExplicitTenantId_SkipsDiscovery()
    {
        var factory = new Mock<IHttpClientFactory>();
        var cache = CreateCache();
        var provider = new DynamicsAuthProvider(factory.Object, cache, NullLogger<DynamicsAuthProvider>.Instance);

        // Username/password akışında da explicit tenant_id ile discovery yapılmamalı
        await Assert.ThrowsAnyAsync<Exception>(() =>
            provider.AcquireTokenAsync(MakeUserPasswordTenant(tenantId: ExpectedTenantId)));

        factory.Verify(f => f.CreateClient("DynamicsDiscovery"), Times.Never);
    }

    [Fact]
    public async Task DiscoverTenantId_MissingHeader_ThrowsTenantAuthException()
    {
        var emptyResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var factory = CreateHttpFactory(emptyResponse);
        var provider = new DynamicsAuthProvider(factory, CreateCache(), NullLogger<DynamicsAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<TenantAuthException>(() =>
            provider.AcquireTokenAsync(MakeClientSecretTenant(tenantId: null)));

        Assert.Contains("Tenant ID keşfedilemedi", ex.Message);
    }

    [Fact]
    public async Task AcquireToken_NoCredentials_ThrowsTenantAuthException()
    {
        var factory = new Mock<IHttpClientFactory>();
        var cache = CreateCache();
        var provider = new DynamicsAuthProvider(factory.Object, cache, NullLogger<DynamicsAuthProvider>.Instance);

        var tenant = new TenantConfig
        {
            TenantId = ExpectedTenantId,
            DynamicsUrl = DynamicsUrl,
            ClientId = "client-id",
            // ClientSecret yok, Username/Password yok
            DeploymentType = DeploymentType.Online
        };

        var ex = await Assert.ThrowsAsync<TenantAuthException>(() =>
            provider.AcquireTokenAsync(tenant));

        Assert.Contains("client_secret VEYA username+password", ex.Message);
    }

    [Fact]
    public async Task AcquireToken_OnPrem_ThrowsNotImplemented()
    {
        var factory = new Mock<IHttpClientFactory>();
        var provider = new DynamicsAuthProvider(factory.Object, CreateCache(), NullLogger<DynamicsAuthProvider>.Instance);

        var tenant = new TenantConfig
        {
            DynamicsUrl = "http://crm-onprem/org",
            ClientId = "id",
            ClientSecret = "secret",
            DeploymentType = DeploymentType.OnPrem
        };

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            provider.AcquireTokenAsync(tenant));
    }
}
