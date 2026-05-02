# LogAnalyzer

**LogAnalyzer** is a .NET 8 backend that combines **central log retrieval**, **structured persistence**, and **OpenAI-assisted analysis**. It supports an on-demand HTTP API for ad-hoc log payloads and a **scheduled pipeline** that pulls from **Graylog**, calls **OpenAI Chat Completions** once per batch (with caching), and stores aggregate runs for review.

---

## Overview

| Path | Role |
|------|------|
| **API** | Clients submit raw log text; the service fingerprints lines, uses cache when possible, and enqueues analysis through a bounded channel. |
| **Periodic job** | A hosted service fetches logs from Graylog, deduplicates by line hash, batches uncached lines, calls OpenAI for a **single consolidated summary**, and writes results to PostgreSQL. |
| **Persistence** | `LogAnalyses` cache (unique `LogHash`), `LogAnalysisRuns` snapshots, optional Graylog checkpoint metadata. |

The AI layer is tuned for **operator summaries**: one JSON result describing themes, severity, and **actionable remediation** (including file/line hints when stack traces are present). Legacy multi-object model output is merged server-side when needed.

---

## Solution layout

| Project | Responsibility |
|---------|----------------|
| `LogAnalyzer.Api` | ASP.NET Core host, DI, Swagger, controllers. |
| `LogAnalyzer.Domain` | Models, `ILogProvider`, repositories, orchestration contracts. |
| `LogAnalyzer.Processor` | Queue consumer, `LogAnalysisOrchestrator`, `PeriodicLogAnalysisBackgroundService`. |
| `LogAnalyzer.Infrastructure` | EF Core (PostgreSQL), Graylog HTTP provider, repositories, parsers, grouping. |
| `LogAnalyzer.AI` | `OpenAiLogAnalyzer` — Chat Completions, rate-limit handling, response parsing. |
| `mock-log-producer` | Optional console app emitting realistic, stack-trace–style logs to Graylog via GELF. |

---

## Features

- **OpenAI Chat Completions** (configurable model; default suitable for cost-sensitive workloads).
- **Per-line fingerprint cache** for API-driven analysis; **batch-hash cache** for periodic Graylog cycles (one row per OpenAI batch, not per duplicated summary).
- **Bounded async queue** for `/api/log/analyze` with back-pressure (`429` when full).
- **Graylog** integration: query window, pagination, authentication options aligned with your deployment.
- **PostgreSQL** via EF Core (`EnsureCreated` on startup in current template — consider migrations for production).
- **Run history API**: latest periodic/OpenAI snapshots via `GET /log-analysis`.
- **Operational logging**: outbound prompt body (length-capped), assistant reply snippets, structured handling of `429` / quota-related errors.

---

## Tech stack

- .NET 8, ASP.NET Core Web API  
- Entity Framework Core + **Npgsql**  
- **OpenAI** HTTP API (`chat/completions`)  
- **Graylog** (REST search + optional GELF from mock producer)  
- Docker Compose (Postgres, PgAdmin, API image, mock producer)

---

## Prerequisites

- [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)+  
- **PostgreSQL** (local or container)  
- **OpenAI API key** with billing/quota appropriate for your tier  
- **Graylog** reachable from the API (host networking differs for Docker vs localhost — see `.env.example`)

---

## Configuration

Secrets should live in **environment variables** or **`.env`** (Compose); avoid committing real keys.

### Core keys

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `OpenAI:ApiKey` | Bearer token for OpenAI |
| `OpenAI:Model` | Chat model id (e.g. `gpt-4o-mini`) |
| `OpenAI:MaxLogCharacters` | Max characters of log payload embedded in the user prompt |
| `OpenAI:MaxPromptLogCharacters` | Max characters of the **logged** prompt copy (Docker/stdout); does not truncate the HTTP body unless you align truncation separately via `MaxLogCharacters` |
| `Graylog:BaseUrl` | Graylog root URL |
| `Graylog:ApiToken` | API token or credential expected by your Graylog setup |
| `PeriodicAnalysis:IntervalMinutes` | Periodic Graylog/OpenAI cycle interval |
| `PeriodicAnalysis:MaxDistinctLines` | Cap distinct lines per cycle |
| `PeriodicAnalysis:MaxLinesSentToOpenAi` | Max uncached lines sent in one OpenAI batch |

Copy `.env.example` → `.env` and adjust. Compose wires DB URL explicitly for `log-analyzer`; OpenAI and Graylog typically come from `env_file: .env`.

---

## Run locally

1. Start PostgreSQL and ensure `ConnectionStrings:DefaultConnection` matches.  
2. Set `OpenAI:ApiKey`, `Graylog:BaseUrl`, `Graylog:ApiToken` (user secrets, env, or `appsettings.Development.json` — never commit secrets).  
3. From the repository root:

```bash
dotnet run --project LogAnalyzer/LogAnalyzer.Api.csproj
```

Swagger (Development):

- HTTPS profile: `https://localhost:7225/swagger`  
- HTTP: `http://localhost:5294/swagger`

---

## Run with Docker Compose

Graylog itself is **not** defined in the bundled Compose file (often installed separately). The stack brings up **PostgreSQL**, **PgAdmin**, the **API**, and the **mock log producer** (GELF target configurable).

```bash
cp .env.example .env
# Edit .env with OpenAI key and Graylog base URL / token and GELF address
docker compose up --build
```

- API: `http://localhost:8080`  
- Swagger in Development builds only (adjust `ASPNETCORE_ENVIRONMENT` if you need UI in containers).

Ensure `GRAYLOG_GELF_ADDRESS` matches your Graylog GELF UDP input when using `mock-log-producer`.

---

## HTTP API

### Analyze logs (on-demand)

`POST /api/log/analyze`

```json
{
  "logs": "ERROR NullReferenceException at Example()\nWARN Dependency timeout",
  "includeRawAIResponse": false
}
```

Multi-line bodies must use escaped `\n` inside JSON strings. Queue exhaustion returns **429 Too Many Requests**.

### Latest analysis runs

`GET /log-analysis`

Returns persisted snapshots (`LogAnalysisRuns`) from the periodic pipeline / stored analyses.

---

## Security notes

- Rotate any API keys that were ever committed to git.  
- Prefer **environment-specific** configuration over checked-in `appsettings.json` secrets.  
- Logged prompts may contain **PII or secrets** if present in source logs — tune `OpenAI:MaxPromptLogCharacters` or log levels accordingly.

---

## License

This project is intended for learning and development. Align `LICENSE` with your organization’s policy.

---

## Changelog

### 2026-05-01

**Persistence & data model**

- Replaced SQLite-oriented hosting with **PostgreSQL** via **EF Core + Npgsql** (`ConnectionStrings:DefaultConnection`).
- Expanded schema beyond ad-hoc cache: **`LogAnalyses`** (`LogAnalysisRecord`, unique **`LogHash`** index), **`LogAnalysisRuns`** (serialized periodic/API-style snapshots), and **`LogSourceCheckpoints`** (incremental Graylog cursor per source).
- Application startup uses **`Database.EnsureCreated()`** in the current template (documented trade-off vs migrations for production).

**Log ingestion (Graylog)**

- Introduced **`ILogProvider`** with **`GraylogLogProvider`**: REST relative search, pagination, bounded result size, timeouts, and HTTP retries aligned with `GraylogOptions`.
- Wired **HTTP Basic** credentials for Graylog API calls where required; resilient extraction of human-readable message text (including `message` field fallbacks typical of Graylog payloads).
- Optional **checkpoint** persistence so incremental reads do not blindly advance on empty cycles.

**AI layer (OpenAI, not Ollama)**

- **`OpenAiLogAnalyzer`** implements **`ILogAnalyzerAI`** using **`HttpClient`** against **`/v1/chat/completions`** (no Semantic Kernel).
- **`OpenAI:Model`** is configurable; outbound log payload size capped via **`OpenAI:MaxLogCharacters`** before embedding in the user prompt.
- **Rate limits & billing**: differentiated **`429`** handling—`Retry-After` when present, exponential backoff otherwise, early termination on **`insufficient_quota`** / **`billing_hard_limit_reached`**, and explicit log guidance toward organization billing/limits URLs.
- **Response parsing**: robust extraction of the first balanced JSON object; support for **JSON arrays** or **multiple comma-separated objects** by merging into a single `LogAnalysisResult`; `JsonException` falls back to heuristic classification.
- **Prompt contract** shifted to a **single consolidated assessment** across batched logs (operator summary), with instructions to surface **stack-derived file:line hints** and **concrete remediation** tied to code locations.

**Processor & orchestration**

- Added **`PeriodicLogAnalysisBackgroundService`**: pulls logs from Graylog on a configurable interval, **deduplicates lines by fingerprint**, splits **cached vs uncached**, caps lines sent to OpenAI, and persists **`LogAnalysisRuns`**.
- **Startup behavior**: first analysis cycle runs **immediately** after host start, then waits `PeriodicAnalysis:IntervalMinutes` (no “silent first interval” gap).
- **Scoped dependencies in singleton hosted services**: periodic path uses **`IServiceScopeFactory`** so repositories and EF contexts resolve correctly per operation.
- **Cache semantics split by path**:
  - **API** (`LogAnalysisOrchestrator`): still **per log line hash** for on-demand `POST /api/log/analyze`.
  - **Periodic**: **one `LogAnalyses` row per OpenAI batch** using a **stable sorted multi-line payload hash** (avoids N duplicate rows sharing identical AI fields); cache hit increments `Count` / `LastSeenUtc` on that batch row.

**API & configuration surface**

- **`Program` validation** for required **`OpenAI:ApiKey`**, **`Graylog:BaseUrl`**, **`Graylog:ApiToken`** at startup.
- **`GET /log-analysis`** endpoint (`LogAnalysisController`) exposing latest persisted runs from **`LogAnalysisRuns`**.
- **Environment-first configuration**: `.env` / Compose-friendly keys (`OpenAI__*`, `Graylog__*`, `PeriodicAnalysis__*`, `GRAYLOG_GELF_ADDRESS`) documented in **`.env.example`**.

**Observability**

- Structured **Information** logs for OpenAI **request dimensions**, **full prompt body** (length-capped via **`OpenAI:MaxPromptLogCharacters`** for Docker/stdout safety), and **assistant output** snippets (with response `id` when returned).

**Developer experience & fixtures**

- **`docker-compose.yml`**: PostgreSQL, PgAdmin, **`log-analyzer`** image build, and **`mock-log-producer`** with configurable **GELF** sink (Graylog itself remains an external dependency in the default compose).
- **`mock-log-producer`** rewritten to emit **production-shaped** log lines: ISO timestamps, service names, `trace_id` / `span_id`, HTTP paths, and **multi-line stack traces** with **`at … in /src/.../File.cs:line N`** style frames for realistic AI evaluation.

**Documentation**

- README rewritten for the **OpenAI + PostgreSQL + Graylog + Docker** architecture; this changelog entry captures the substantive runtime and schema deltas that the short May 1 list previously summarized.

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
