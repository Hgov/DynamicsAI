using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.UseCases;
using DynamicsAI.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

// MCP stdio transport stdout'u JSON-RPC için kullanır.
// Console.Out'a yanlışlıkla yazılan her şeyi stderr'e yönlendir.
// MCP SDK Console.OpenStandardOutput() (ham stream) kullandığından bu yönlendirmeden etkilenmez.
Console.SetOut(Console.Error);

// Claude Desktop farklı bir CWD ile başlatabilir — her zaman exe'nin bulunduğu klasörü kullan.
var baseDir = AppContext.BaseDirectory;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
    .WriteTo.File(Path.Combine(baseDir, "logs", "dynamicsai-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = baseDir   // appsettings.json her zaman exe klasöründen okunur
    });

    // Default console/debug loggers stdout'a yazar — hepsini temizle, sadece Serilog (stderr) kalsın
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    builder.Services.AddInfrastructure(builder.Configuration);

    // DefaultTenantOptions: IOptions<> yerine doğrudan singleton — MCP SDK DI çözümlemesinde sorun çıkarmaz
    var defaultTenant = builder.Configuration
        .GetSection(DefaultTenantOptions.SectionName)
        .Get<DefaultTenantOptions>() ?? new DefaultTenantOptions();
    builder.Services.AddSingleton(defaultTenant);

    builder.Services.AddScoped<GetMetadataUseCase>();
    builder.Services.AddScoped<ExecuteQueryUseCase>();
    builder.Services.AddScoped<ExecuteCrudUseCase>();
    builder.Services.AddScoped<ExportToExcelUseCase>();

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "DynamicsAI MCP Server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
