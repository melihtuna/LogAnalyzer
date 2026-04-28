# LogAnalyzer

`LogAnalyzer` is an AI-powered log intelligence backend built with ASP.NET Core Web API.
It accepts logs, analyzes them with AI, applies safe fallbacks when AI fails, caches results per log entry, and returns a structured response.

## Current Phase (Updated 2026-04-28)

The project is now in **Phase 2: AI-powered Log Intelligence System** with production-hardening updates:

- Layered architecture with separate projects
- Per-log caching (not whole payload)
- Background queue processing with bounded capacity
- AI retry, timeout, and confidence-based fallback
- Improved log grouping normalization
- Critical severity webhook notification (non-blocking)
- Frequency tracking (`Count`, `LastSeenUtc`) for repeated errors

## Solution Structure

- `LogAnalyzer/` -> `LogAnalyzer.Api` (HTTP API, DI, composition root)
- `LogAnalyzer.Domain/` (models and interfaces)
- `LogAnalyzer.Processor/` (orchestration, queue, background processing)
- `LogAnalyzer.Infrastructure/` (EF Core persistence, parser, hashing, grouping, notifications)
- `LogAnalyzer.AI/` (Ollama integration and JSON schema validation)

## Key Features

- `POST /api/log/analyze` endpoint
- Async queue-based processing using `BackgroundService`
- Bounded channel (`capacity: 1000`) with backpressure handling
- Cache lookup by **hash per log entry**
- Aggregated response for multi-line/multi-entry payloads
- AI output contract:
  - `severity` (`critical`, `high`, `medium`, `low`)
  - `category`
  - `summary`
  - `suggestion`
  - `confidence` (`0..1`)
- AI resilience:
  - timeout: `3s`
  - retry: `1`
  - fallback rules if AI fails or confidence remains low
- Repeated-log tracking:
  - `Count` increments
  - `LastSeenUtc` updates
- Critical alerts via webhook without blocking request flow

## Tech Stack

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core (SQLite)
- Swagger / OpenAPI
- HttpClientFactory
- Ollama (`llama3` default)

## Requirements

- .NET SDK 8+
- Running Ollama service
- Installed model (default: `llama3`)

Check model:

```powershell
ollama list
```

## Run the Project

```bash
git clone <repo-url>
cd LogAnalyzer
dotnet run --project LogAnalyzer/LogAnalyzer.Api.csproj
```

Swagger:

- `https://localhost:7225/swagger` (or port from `Properties/launchSettings.json`)

## Configuration

`LogAnalyzer/appsettings.json`:

- `ConnectionStrings:LogAnalyzer` -> SQLite connection string
- `Ollama:Endpoint` -> AI endpoint (default `http://localhost:11434/api/generate`)
- `Ollama:Model` -> model name
- `Webhook:Url` -> optional critical-alert webhook URL

## API

### Endpoint

- `POST /api/log/analyze`

### Request

```json
{
  "logs": "2026-04-28 10:15:32 ERROR NullReferenceException at UserService.GetUserById()\n2026-04-28 10:16:01 ERROR TimeoutException while connecting external API"
}
```

### Response (example)

```json
{
  "severity": "high",
  "category": "application",
  "summary": "Null reference and timeout errors detected in service flow.",
  "suggestion": "Add null guards and review timeout/retry policy for external dependencies.",
  "confidence": 0.72,
  "groupId": "grp-4f23d19a8b2c",
  "isCached": false
}
```

## Notes

- JSON requests must escape line breaks in `logs` as `\n`.
- Queue saturation returns `429 Too Many Requests`.
- AI-related failures do not crash processing; fallback classification is returned instead.

## Changelog

### 2026-04-28

- Refactored to layered architecture: `LogAnalyzer.Api`, `LogAnalyzer.Domain`, `LogAnalyzer.Processor`, `LogAnalyzer.Infrastructure`, `LogAnalyzer.AI`.
- Standardized AI output contract with strict JSON schema and validation.
- Switched to per-log-entry caching and aggregate response generation.
- Added bounded background queue (`capacity: 1000`) with backpressure and `429` handling.
- Added AI timeout (`3s`), single retry, and safe fallback when AI fails or confidence is low.
- Improved grouping normalization by removing timestamps, GUIDs, numeric IDs, and extra whitespace.
- Made critical webhook notifications non-blocking and failure-tolerant.
- Extended persistence model with `Count` and `LastSeenUtc` for repeated log tracking.
- Fixed hosted service lifetime issue by resolving scoped orchestrator inside a created scope.

## License

This project is for learning and development purposes. Add or adjust `LICENSE` based on your repository policy.
