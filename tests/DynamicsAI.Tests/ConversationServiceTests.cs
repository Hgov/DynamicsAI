using DynamicsAI.GatewayApi.Data;
using DynamicsAI.GatewayApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicsAI.Tests;

public class ConversationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _sp;

    public ConversationServiceTests()
    {
        // Shared in-memory SQLite connection — keeps the DB alive across scopes
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opts => opts.UseSqlite(_connection));
        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private ConversationService CreateService() =>
        new(_sp.GetRequiredService<IServiceScopeFactory>());

    [Fact]
    public async Task GetOrCreate_NewSession_CreatesSessionInDb()
    {
        var svc = CreateService();

        var session = await svc.GetOrCreateAsync(null, "user1");

        Assert.NotNull(session);
        Assert.NotEmpty(session.Id);
        Assert.Equal("user1", session.UserId);
        Assert.Empty(session.Messages);
    }

    [Fact]
    public async Task GetOrCreate_ExistingSessionId_ReturnsSameSession()
    {
        var svc = CreateService();
        var first = await svc.GetOrCreateAsync(null, "user1");

        var second = await svc.GetOrCreateAsync(first.Id, "user1");

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetOrCreate_UnknownSessionId_CreatesNewSessionWithSameId()
    {
        var svc = CreateService();

        // Bilinmeyen session_id gelirse, o ID korunarak yeni session açılır
        var session = await svc.GetOrCreateAsync("my-custom-id", "user1");

        Assert.NotNull(session);
        Assert.Equal("my-custom-id", session.Id);
    }

    [Fact]
    public async Task FlushAsync_PersistsMessagesToDb()
    {
        var svc = CreateService();
        var session = await svc.GetOrCreateAsync(null, "user1");

        session.Messages.Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{"role":"user","content":"Merhaba"}""")!);
        session.Messages.Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{"role":"assistant","content":"Nasıl yardımcı olabilirim?"}""")!);

        await svc.FlushAsync(session.Id);

        var messages = await svc.GetSessionMessagesAsync(session.Id);
        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("assistant", messages[1].Role);
    }

    [Fact]
    public async Task FlushAsync_SetsTitleFromFirstUserMessage()
    {
        var svc = CreateService();
        var session = await svc.GetOrCreateAsync(null, "user1");

        session.Messages.Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{"role":"user","content":"Account sayısı kaç?"}""")!);
        await svc.FlushAsync(session.Id);

        var sessions = await svc.GetUserSessionsAsync("user1");
        Assert.Equal("Account sayısı kaç?", sessions[0].Title);
    }

    [Fact]
    public async Task GetOrCreate_LoadsMessagesFromDb_AfterCacheEvict()
    {
        var svc1 = CreateService();
        var session = await svc1.GetOrCreateAsync(null, "user1");
        session.Messages.Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{"role":"user","content":"Test mesajı"}""")!);
        await svc1.FlushAsync(session.Id);

        // Yeni service instance → cache boş, DB'den yüklenmeli
        var svc2 = CreateService();
        var loaded = await svc2.GetOrCreateAsync(session.Id, "user1");

        Assert.Single(loaded.Messages);
        Assert.Equal(1, loaded.PersistedCount);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSessionAndMessages()
    {
        var svc = CreateService();
        var session = await svc.GetOrCreateAsync(null, "user1");
        session.Messages.Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{"role":"user","content":"Silinecek"}""")!);
        await svc.FlushAsync(session.Id);

        var deleted = await svc.DeleteAsync(session.Id);

        Assert.True(deleted);
        var messages = await svc.GetSessionMessagesAsync(session.Id);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task GetUserSessionsAsync_ReturnsOnlyUserSessions()
    {
        var svc = CreateService();
        await svc.GetOrCreateAsync(null, "userA");
        await svc.GetOrCreateAsync(null, "userA");
        await svc.GetOrCreateAsync(null, "userB");

        var userASessions = await svc.GetUserSessionsAsync("userA");

        Assert.Equal(2, userASessions.Count);
        Assert.All(userASessions, s => Assert.Equal("userA", s.UserId));
    }
}
