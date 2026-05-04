namespace DynamicsAI.GatewayApi;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string BasePath { get; set; } = "files";

    private string Resolved => Path.IsPathRooted(BasePath)
        ? BasePath
        : Path.Combine(AppContext.BaseDirectory, BasePath);

    public string ExportsPath => Path.Combine(Resolved, "exports");
    public string UploadsPath => Path.Combine(Resolved, "uploads");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ExportsPath);
        Directory.CreateDirectory(UploadsPath);
    }
}
