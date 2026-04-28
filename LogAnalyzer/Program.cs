using System.Threading.Channels;
using LogAnalyzer.AI;
using LogAnalyzer.AI.Options;
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(WebhookOptions.SectionName));

builder.Services.AddDbContext<LogAnalyzerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("LogAnalyzer") ?? "Data Source=loganalyzer.db"));

builder.Services.AddSingleton(Channel.CreateBounded<QueuedLogAnalysisRequest>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,
    SingleWriter = false
}));
builder.Services.AddSingleton<ILogAnalysisQueue, ChannelLogAnalysisQueue>();
builder.Services.AddScoped<ILogAnalysisOrchestrator, LogAnalysisOrchestrator>();
builder.Services.AddHostedService<LogAnalysisBackgroundService>();

builder.Services.AddScoped<ILogAnalysisRepository, LogAnalysisRepository>();
builder.Services.AddSingleton<ILogFingerprintService, Sha256LogFingerprintService>();
builder.Services.AddSingleton<ILogGroupingService, SimpleLogGroupingService>();
builder.Services.AddSingleton<ILogParser, LogParser>();

builder.Services.AddHttpClient<ILogAnalyzerAI, OllamaLogAnalyzerAI>();
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
