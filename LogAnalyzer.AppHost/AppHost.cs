var builder = DistributedApplication.CreateBuilder(args);

// References LogAnalyzer.Api via AppHost SDK project metadata (Projects.*).
// Postgres remains external (e.g. Docker Compose); connection string is unchanged on the API.
builder.AddProject<Projects.LogAnalyzer_Api>("loganalyzer-api");

builder.Build().Run();
