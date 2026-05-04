using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DynamicsAI.GatewayApi.Data;
using DynamicsAI.GatewayApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicsAI.GatewayApi.Services;

public class ConversationService(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<string, ConversationSession> _cache = new();

    // Mevcut session'ı cache'den veya DB'den yükler; yoksa yeni oluşturur.
    public async Task<ConversationSession> GetOrCreateAsync(string? sessionId, string userId)
    {
        if (sessionId is not null && _cache.TryGetValue(sessionId, out var cached))
            return cached;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (sessionId is not null)
        {
            var existing = await db.Sessions
                .Include(s => s.Messages.OrderBy(m => m.CreatedAt))
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (existing is not null)
            {
                var loaded = new ConversationSession(existing.Id, existing.UserId);
                foreach (var msg in existing.Messages)
                    loaded.Messages.Add(JsonNode.Parse(msg.ContentJson)!);
                loaded.PersistedCount = loaded.Messages.Count;
                _cache[existing.Id] = loaded;
                return loaded;
            }
        }

        var newId = sessionId ?? Guid.NewGuid().ToString("N")[..16];
        var session = new ConversationSession(newId, userId);

        db.Sessions.Add(new SessionEntity
        {
            Id            = newId,
            UserId        = userId,
            CreatedAt     = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        _cache[newId] = session;
        return session;
    }

    // Bellekteki yeni mesajları DB'ye yazar (her SendMessageAsync sonunda çağrılır).
    public async Task FlushAsync(string sessionId)
    {
        if (!_cache.TryGetValue(sessionId, out var session)) return;

        var newMessages = session.Messages.Skip(session.PersistedCount).ToList();
        if (newMessages.Count == 0) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dbSession = await db.Sessions.FindAsync(sessionId);
        if (dbSession is not null)
        {
            // İlk kullanıcı mesajından başlık üret
            if (dbSession.Title is null)
            {
                var firstUser = session.Messages.FirstOrDefault(
                    m => m["role"]?.GetValue<string>() == "user");
                if (firstUser?["content"] is JsonValue v && v.TryGetValue<string>(out var text))
                    dbSession.Title = text.Length > 80 ? text[..80] + "…" : text;
            }
            dbSession.LastActivityAt = DateTime.UtcNow;
        }

        var now = DateTime.UtcNow;
        foreach (var msg in newMessages)
        {
            db.Messages.Add(new MessageEntity
            {
                SessionId     = sessionId,
                Role          = msg["role"]?.GetValue<string>() ?? "user",
                ContentJson   = msg.ToJsonString(),
                IsToolMessage = IsToolMessage(msg),
                CreatedAt     = now
            });
        }

        await db.SaveChangesAsync();
        session.PersistedCount = session.Messages.Count;
    }

    public async Task<bool> DeleteAsync(string sessionId)
    {
        _cache.TryRemove(sessionId, out _);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.Sessions.FindAsync(sessionId);
        if (entity is null) return false;

        db.Sessions.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<SessionSummary>> GetUserSessionsAsync(string userId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.LastActivityAt)
            .Select(s => new SessionSummary
            {
                SessionId       = s.Id,
                UserId          = s.UserId,
                Title           = s.Title,
                CreatedAt       = s.CreatedAt,
                LastActivityAt  = s.LastActivityAt,
                MessageCount    = s.Messages.Count
            })
            .ToListAsync();
    }

    public async Task<List<MessageDto>> GetSessionMessagesAsync(string sessionId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Messages
            .Where(m => m.SessionId == sessionId && !m.IsToolMessage)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto
            {
                Role        = m.Role,
                ContentJson = m.ContentJson,
                CreatedAt   = m.CreatedAt
            })
            .ToListAsync();
    }

    public int ActiveSessionCount => _cache.Count;

    // content bir dizi ama içinde hiç "text" bloğu yoksa → tool ara adımı
    private static bool IsToolMessage(JsonNode msg)
    {
        var content = msg["content"];
        if (content is not JsonArray arr) return false;
        return !arr.Any(b => b?["type"]?.GetValue<string>() == "text");
    }
}

public class ConversationSession(string id, string userId)
{
    public string Id { get; } = id;
    public string UserId { get; } = userId;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public List<JsonNode> Messages { get; } = [];
    public int PersistedCount { get; set; }
}

public class SessionSummary
{
    public string SessionId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string? Title { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
    public int MessageCount { get; init; }
}

public class MessageDto
{
    public string Role { get; init; } = "";
    public string ContentJson { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}
