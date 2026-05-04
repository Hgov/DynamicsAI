namespace DynamicsAI.GatewayApi.Data.Entities;

public class SessionEntity
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string? Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public List<MessageEntity> Messages { get; set; } = [];
}
