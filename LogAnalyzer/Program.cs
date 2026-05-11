using System.Threading.Channels;
using LogAnalyzer.AI;
using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Infrastructure.Notifications;
using LogAnalyzer.Infrastructure.Options;
using LogAnalyzer.Infrastructure.Jira;
using LogAnalyzer.Infrastructure.Outbound;
using LogAnalyzer.Infrastructure.Persistence;
using LogAnalyzer.Infrastructure.Repositories;
using LogAnalyzer.Infrastructure.Services;
using LogAnalyzer.Processor;
using LogAnalyzer.Processor.Queue;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
ValidateRequiredConfiguration(builder.Configuration);
ValidateJiraOutboundConfiguration(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<GraylogOptions>(builder.Configuration.GetSection(GraylogOptions.SectionName));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(WebhookOptions.SectionName));
builder.Services.Configure<IncidentReuseOptions>(builder.Configuration.GetSection(IncidentReuseOptions.SectionName));
builder.Services.Configure<IncidentAiSnapshotOptions>(builder.Configuration.GetSection(IncidentAiSnapshotOptions.SectionName));

builder.Services.AddLogAnalyzerOutboundIntegration(builder.Configuration);

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
builder.Services.AddSingleton<IIncidentFingerprintGenerator, IncidentFingerprintGenerator>();
builder.Services.AddScoped<IIncidentRepository, IncidentRepository>();
builder.Services.AddScoped<IIncidentUpsertService, IncidentUpsertService>();
builder.Services.AddScoped<IBatchIncidentCandidatesRepository, BatchIncidentCandidatesRepository>();
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
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();
app.Run();

static void ValidateJiraOutboundConfiguration(IConfiguration configuration)
{
    var opts = configuration.GetSection(JiraOptions.SectionName).Get<JiraOptions>();
    if (opts is null || !opts.EnableIntegration || opts.UseMockClient)
    {
        return;
    }

    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(opts.BaseUrl))
    {
        missing.Add("Jira:BaseUrl");
    }

    if (string.IsNullOrWhiteSpace(opts.ProjectKey))
    {
        missing.Add("Jira:ProjectKey");
    }

    switch (opts.AuthKind)
    {
        case JiraAuthKind.BearerPat:
            if (string.IsNullOrWhiteSpace(opts.PersonalAccessToken))
            {
                missing.Add("Jira:PersonalAccessToken");
            }

            break;
        default:
            if (string.IsNullOrWhiteSpace(opts.Email))
            {
                missing.Add("Jira:Email");
            }

            if (string.IsNullOrWhiteSpace(opts.ApiToken))
            {
                missing.Add("Jira:ApiToken");
            }

            break;
    }

    if (missing.Count == 0)
    {
        return;
    }

    using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
    var logger = loggerFactory.CreateLogger("StartupValidation");
    foreach (var key in missing)
    {
        logger.LogCritical(
            "Jira outbound REST enabled but configuration is incomplete for production: {SettingKey}",
            key);
    }

    throw new InvalidOperationException(
        $"Jira outbound REST integration enabled (EnableIntegration=true, UseMockClient=false) but settings are missing: {string.Join(", ", missing)}");
}

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
