namespace DynamicsAI.GatewayApi.Data.Entities;

public class ExportedFileEntity
{
    public string Id        { get; set; } = "";
    public string FilePath  { get; set; } = "";
    public string Category  { get; set; } = "export"; // "export" | "upload"
    public DateTime CreatedAt { get; set; }
}
