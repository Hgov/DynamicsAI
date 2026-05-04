namespace DynamicsAI.GatewayApi.Data.Entities;

public class MessageEntity
{
    public int Id { get; set; }
    public string SessionId { get; set; } = "";
    public string Role { get; set; } = "";
    public string ContentJson { get; set; } = "";
    public bool IsToolMessage { get; set; }   // tool_use / tool_result ara adımları gizler
    public DateTime CreatedAt { get; set; }
    public SessionEntity Session { get; set; } = null!;
}
