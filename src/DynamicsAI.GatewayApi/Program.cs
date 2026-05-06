using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.UseCases;
using DynamicsAI.GatewayApi;
using DynamicsAI.GatewayApi.Data;
using DynamicsAI.GatewayApi.Services;
using DynamicsAI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/gatewayapi-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // JSON body'de base64 dosya (~20 MB raw → ~27 MB base64) için body limit artırıldı
    builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 30 * 1024 * 1024);

    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
            opts.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()));
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
        c.SwaggerDoc("v1", new() { Title = "DynamicsAI Gateway API", Version = "v1" }));

    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

    builder.Services.AddInfrastructure(builder.Configuration);

    var defaultTenant = builder.Configuration
        .GetSection(DefaultTenantOptions.SectionName)
        .Get<DefaultTenantOptions>() ?? new DefaultTenantOptions();
    builder.Services.AddSingleton(defaultTenant);

    var storageOptions = builder.Configuration
        .GetSection(StorageOptions.SectionName)
        .Get<StorageOptions>() ?? new StorageOptions();
    storageOptions.EnsureDirectories(); // exports/ ve uploads/ klasörlerini oluştur
    builder.Services.AddSingleton(storageOptions);

    builder.Services.AddScoped<GetMetadataUseCase>();
    builder.Services.AddScoped<ExecuteQueryUseCase>();
    builder.Services.AddScoped<ExecuteCrudUseCase>();
    builder.Services.AddScoped<ExportToExcelUseCase>();

    var dbPath = Path.Combine(AppContext.BaseDirectory, "conversations.db");
    builder.Services.AddDbContext<AppDbContext>(opts =>
        opts.UseSqlite($"Data Source={dbPath}"));

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<ConversationService>();
    builder.Services.AddSingleton<ExportedFileRegistry>();
    builder.Services.AddSingleton<FileProcessingService>();
    builder.Services.AddScoped<DynamicsToolExecutor>();
    builder.Services.AddScoped<ClaudeAgentService>();

    builder.Services.AddHttpClient("ClaudeApi", c =>
    {
        c.BaseAddress = new Uri("https://api.anthropic.com");
        c.Timeout = TimeSpan.FromMinutes(5);
    });

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // ExportedFiles tablosu — idempotent oluştur
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ExportedFiles (
                Id        TEXT NOT NULL PRIMARY KEY,
                FilePath  TEXT NOT NULL,
                Category  TEXT NOT NULL DEFAULT 'export',
                CreatedAt TEXT NOT NULL
            )
        """);

        // Mevcut DB'ye Category sütunu ekle — önce var mı diye kontrol et
        var cols = db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('ExportedFiles')")
            .ToList();
        if (!cols.Contains("Category"))
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE ExportedFiles ADD COLUMN Category TEXT NOT NULL DEFAULT 'export'");
            Log.Information("ExportedFiles.Category sütunu eklendi");
        }
    }

    app.UseCors();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DynamicsAI Gateway API v1"));
    app.UseAuthorization();
    app.MapControllers();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Gateway API beklenmedik şekilde sonlandı");
}
finally
{
    Log.CloseAndFlush();
}
