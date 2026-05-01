using System.Threading.Channels;
using LogAnalyzer.AI;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Infrastructure.Notifications;
using LogAnalyzer.Infrastructure.Options;
using LogAnalyzer.Infrastructure.Persistence;
using LogAnalyzer.Infrastructure.Repositories;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.Processor;
using LogAnalyzer.Processor.Queue;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
ValidateRequiredConfiguration(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<GraylogOptions>(builder.Configuration.GetSection(GraylogOptions.SectionName));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(WebhookOptions.SectionName));

builder.Services.AddDbContext<LogAnalyzerDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=loganalyzer;Username=postgres;Password=postgres"));

builder.Services.AddSingleton(Channel.CreateBounded<QueuedLogAnalysisRequest>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,
    SingleWriter = false
}));
builder.Services.AddSingleton<ILogAnalysisQueue, ChannelLogAnalysisQueue>();
builder.Services.AddScoped<ILogAnalysisOrchestrator, LogAnalysisOrchestrator>();
builder.Services.AddHostedService<LogAnalysisBackgroundService>();
builder.Services.AddHostedService<PeriodicLogAnalysisBackgroundService>();

builder.Services.AddScoped<ILogAnalysisRepository, LogAnalysisRepository>();
builder.Services.AddScoped<ILogAnalysisRunRepository, LogAnalysisRunRepository>();
builder.Services.AddScoped<ILogSourceCheckpointRepository, LogSourceCheckpointRepository>();
builder.Services.AddSingleton<ILogFingerprintService, Sha256LogFingerprintService>();
builder.Services.AddSingleton<ILogGroupingService, SimpleLogGroupingService>();
builder.Services.AddSingleton<ILogParser, LogParser>();
builder.Services.AddHttpClient<ILogProvider, GraylogLogProvider>();

builder.Services.AddHttpClient<ILogAnalyzerAI, OpenAiLogAnalyzer>();
builder.Services.AddHttpClient<INotificationService, WebhookNotificationService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<LogAnalyzerDbContext>();
    dbContext.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

static void ValidateRequiredConfiguration(IConfiguration configuration)
{
    var requiredKeys = new[]
    {
        "OpenAI:ApiKey",
        "Graylog:BaseUrl",
        "Graylog:ApiToken"
    };

    var missingKeys = requiredKeys
        .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
        .ToList();

    if (missingKeys.Count == 0)
    {
        return;
    }

    using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
    var logger = loggerFactory.CreateLogger("StartupValidation");
    foreach (var key in missingKeys)
    {
        logger.LogCritical("Missing required configuration setting: {SettingKey}", key);
    }

    throw new InvalidOperationException(
        $"Missing required configuration settings: {string.Join(", ", missingKeys)}");
}
