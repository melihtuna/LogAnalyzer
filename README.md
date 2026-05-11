# LogAnalyzer

.NET 8 API: Graylog → OpenAI classification → PostgreSQL incidents → optional Jira outbound. On-demand `POST /api/log/analyze` uses a bounded queue.

---

## Architecture Overview

| Component | Role |
|-----------|------|
| **LogAnalyzer.Api** | HTTP host, EF migrations, controllers. |
| **LogAnalyzer.Processor** | Queue worker + `PeriodicLogAnalysisBackgroundService` (Graylog batch → OpenAI when needed). |
| **LogAnalyzer.Infrastructure** | Npgsql, Graylog client, incidents, outbound channel + Jira REST. |
| **LogAnalyzer.AI** | OpenAI Chat Completions client. |
| **LogAnalyzer.ServiceDefaults** | OTEL (`LogAnalyzer.Outbound` meter), `/health`, `/alive`. |
| **LogAnalyzer.AppHost** | Runs **LogAnalyzer.Api** locally + Aspire Dashboard. |
| **Docker Compose** | **Postgres**, **pgAdmin**, **mock-log-producer** (GELF → Graylog). **No API container.** |

**Periodic pipeline:** dedupe → **uncached lines partitioned by operational fingerprint** (evidence-based `groupId`) → **one OpenAI `AnalyzeAsync` per group** (similar lines share one prompt) → **one `LogAnalyses` row per line** (line-hash cache) + **one incident upsert per group** (separate Jira tasks per operational group) → outbound enqueue when enabled. OpenAI call volume capped by **`PeriodicAnalysis:MaxOpenAiCallsPerCycle`**. First cycle **after** `PeriodicAnalysis:IntervalMinutes` (no run at process start).

---

## Local model (default)

| Layer | What runs |
|-------|-----------|
| **Compose** | Database + pgAdmin + producer sending synthetic logs toward Graylog via **GELF**. |
| **AppHost / VS** | API process + Aspire Dashboard + tracing defaults. |

Do **not** expect the API inside Compose for day-to-day dev. Optional production-style image build still uses repo **`Dockerfile`** (manual `docker build`), not `docker compose`.

---

## Configuration split

**Graylog uses two different surfaces:**

| Surface | Purpose | Where |
|---------|---------|--------|
| **GELF UDP** | Docker **logging driver** for `mock-log-producer` only — where log lines are **sent into** Graylog. | **`.env`** → **`GRAYLOG_GELF_ADDRESS`** (Compose substitution). **Not** read by ASP.NET `GraylogOptions`. |
| **REST API** | API periodic job **searches** Graylog (`GraylogLogProvider`). | **`Graylog`** in **`appsettings*.json`** / user-secrets (`BaseUrl`, `ApiToken`, `Query`, timeouts, pagination, …). |

**Jira:** `Jira` section in **`appsettings.json`** (defaults) and **`appsettings.Development.json`** (local overrides) or **`dotnet user-secrets`** for tokens — never in **`.env`** for this repo’s Compose layout.

### 1) Docker / Compose — containers only

| Concern | Files |
|---------|--------|
| **Postgres** | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` in **`.env.example`** → **`.env`**, substituted into **`docker-compose.yml`**. |
| **pgAdmin** | `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_DEFAULT_PASSWORD` |
| **mock-log-producer → Graylog** | **`GRAYLOG_GELF_ADDRESS`** (GELF UDP endpoint Docker-side). Producer binary itself has no extra env in this stack. |

No `OpenAI__`, `Graylog__`, `Jira__`, `PeriodicAnalysis__`, `ConnectionStrings__` (and no ASP.NET double-underscore vars) in **`.env`**.

**DB pairing:** `POSTGRES_*` must match **`ConnectionStrings:DefaultConnection`** (database name, user, password on `localhost`) in **`appsettings.json`**.

### 2) Application — API + shared processors

| Concern | Files |
|---------|--------|
| **ConnectionStrings**, **OpenAI**, **Graylog** (REST), **PeriodicAnalysis** (interval, line caps, **`MaxOpenAiCallsPerCycle`**), **IncidentReuse**, **IncidentAiSnapshot**, **Webhook**, **Jira**, host **Logging** / **AllowedHosts** | **`LogAnalyzer/appsettings.json`** |
| Local/dev overrides | **`LogAnalyzer/appsettings.Development.json`** (merged over **`appsettings.json`**). For shared repos prefer **`dotnet user-secrets`** instead of committing secrets here. |
| Secrets (`OpenAI:ApiKey`, `Graylog:ApiToken`, Jira tokens, …) | **`dotnet user-secrets`** on **`LogAnalyzer.Api`** |

### 3) Aspire host only

| Concern | Files |
|---------|--------|
| AppHost / DCP log noise | **`LogAnalyzer.AppHost/appsettings.json`** (and **`.Development.json`** if present). Does **not** replace **`LogAnalyzer/appsettings*.json`** for the API. |

**`LogAnalyzer/configuration.template.json`** — same JSON as **`appsettings.json`** (secrets empty); reference / diff only — **never loaded** by the runtime.

```bash
dotnet user-secrets set "OpenAI:ApiKey" "<key>" --project LogAnalyzer/LogAnalyzer.Api.csproj
dotnet user-secrets set "Graylog:ApiToken" "<token>" --project LogAnalyzer/LogAnalyzer.Api.csproj
```

---

## End-to-end observation flow

1. **`docker compose up -d`** — Postgres, pgAdmin, mock-log-producer.
2. **Graylog** running with a **GELF UDP input** matching producer endpoint (default `udp://host.docker.internal:12201` from inside mock container).
3. **Visual Studio or** `dotnet run --project LogAnalyzer.AppHost/LogAnalyzer.AppHost.csproj` — API starts; console shows **Aspire Dashboard** URL.
4. **mock-log-producer** emits structured lines → Graylog indexes them.
5. **`PeriodicLogAnalysisBackgroundService`** (after first interval) pulls Graylog → **per operational group** OpenAI → **`LogAnalyses` / incidents** (ensure `Graylog:*` + `OpenAI:*` configured).
6. **Jira outbound** — if `Jira:EnableIntegration` is **true**, dispatcher processes queue → mock issue key (**`UseMockClient: true`**) or REST create (**`UseMockClient: false`** + valid auth). Watch logs, `/health`, OTEL meter **`LogAnalyzer.Outbound`**.

Shortcut without Graylog delay: **`POST /api/log/analyze`** with a JSON body (still exercises OpenAI path).

---

## Startup order

1. Graylog (external) + GELF input.
2. `docker compose up -d`
3. AppHost or `dotnet run` API — EF migrations apply on startup.

Ports (typical): Postgres **5432**, pgAdmin **5050**, API **7225/5294** (VS profiles), Swagger under Development.

---

## OpenAI & Jira

- **OpenAI:** `OpenAI:Model`, caps in **`appsettings.json`**; **`OpenAI:ApiKey`** via user-secrets (never commit real keys in JSON). Optional **`OpenAI:Organization`** / **`OpenAI:Project`** → `OpenAI-Organization` / `OpenAI-Project` request headers when set.
- **Jira:** **`Jira`** section in appsettings. **`EnableIntegration: false`** → queue/dispatcher idle. **`UseMockClient: true`** → no HTTP; deterministic fake keys. **`UseMockClient: false`** + **`EnableIntegration: true`** → REST; startup validates BaseUrl, ProjectKey, Basic or Bearer auth fields.

---

## Aspire Dashboard

When AppHost starts, the **dashboard URL is printed to the console** (Aspire assigns the port). Use it for resource status, structured logs, and traces emitted via **`AddServiceDefaults`**.

---

## Smoke tests

```bash
dotnet build LogAnalyzer.slnx -c Release
dotnet test LogAnalyzer.Infrastructure.Tests/LogAnalyzer.Infrastructure.Tests.csproj -c Release --no-build
```

**HTTP:** `GET /alive`, `GET /health`, `POST /api/log/analyze`, `GET /log-analysis`.

---

## Observability & health

- OpenTelemetry metrics/tracing via ServiceDefaults; **`LogAnalyzer.Outbound`** for queue/dispatch/Jira HTTP.
- **`GET /health`** includes outbound readiness (`ready` tag) when Jira integration is registered.

---

## Troubleshooting

| Symptom | Check |
|---------|--------|
| Startup validation fails | `OpenAI:ApiKey`, `Graylog:BaseUrl`, `Graylog:ApiToken` (**user-secrets** or temporary edits to **`appsettings.Development.json`** — avoid committing secrets). |
| Periodic never hits OpenAI | Graylog empty/query window; unchanged aggregate log hash; all lines cached; interval not elapsed; **`MaxOpenAiCallsPerCycle`** exhausted (remaining uncached lines wait next cycle). |
| No logs in Graylog from producer | GELF input port/host vs `GRAYLOG_GELF_ADDRESS`; firewall. |
| `429` on analyze | Analysis queue full (1000). |

---

## Security

Do not commit secrets. Prefer **user-secrets**. If an old **`.env`** ever contained API keys, **rotate them** — `.env` must stay Compose-only (see **`.env.example`**).

---

## License

Placeholder — align with your organization.

---

## Changelog (series)

### Phase 5 — Periodic per-group AI, fingerprint & Jira presentation (2026-05-11)

- **Periodic analysis (breaking behavior vs. prior batch):** Uncached lines are **clustered by evidence-only operational fingerprint** (`OperationalIncidentFingerprintHeuristics.ComputeGroupIdFromEvidenceOnly` → `ILogGroupingService`). Each cluster gets **its own** `AnalyzeAsync` (similar lines stay in one prompt). **Each line** still gets a **`LogAnalyses`** row keyed by **stable line hash** (cache preserved). **Each cluster** drives **one** incident upsert → **one Jira issue** per operational group (not one mega-batch issue). OpenAI volume per cycle is limited by **`PeriodicAnalysis:MaxOpenAiCallsPerCycle`** (default 16); overflow lines are deferred with a warning.
- **Removed from periodic:** `EnableMultiCandidate`, single mega-batch `AnalyzeAsync`, and **`AnalyzeBatchCandidatesAsync` / `BatchIncidentCandidates`** cache usage (the table and AI method remain in the solution for other scenarios).
- **Fingerprint:** Canonical incident grouping is **evidence-derived** (service/component/issue class, TLS CN, SQL state hints); AI candidate titles are no longer part of `groupId` for periodic grouping.
- **Incidents & Jira:** `Incidents` gained **`OperationalTitle`** and **`EvidenceLogExcerpt`** (migration `AddIncidentOperationalPresentationFields`); periodic upserts pass **`IncidentUpsertPresentation`**. **`JiraIssueDescriptionFormatter`** adds optional **Operational title** / **Evidence** sections, uses **`OperationalTitle`** for the Jira issue summary when present, and labels model text **AI technical summary**.
- **OpenAI HTTP:** Optional **`OpenAI:Organization`** and **`OpenAI:Project`** configuration maps to OpenAI platform headers.

### Phase 4c — Config boundary cleanup (2026-05-10)

- **`.env` / `.env.example`**: strictly Compose (Postgres, pgAdmin, **`GRAYLOG_GELF_ADDRESS`**). Removed ASP.NET-style variables from `.env`.
- **`appsettings.Development.json`**: local/demo overrides (OpenAI, Graylog REST, periodic tuning); production forks should use **user-secrets** and keep this file non-secret or gitignored.
- **`configuration.template.json`** kept in lockstep with **`appsettings.json`** (application surface only).

### Phase 4b — Local orchestration simplification (2026-05-10)

- **Docker Compose:** Postgres + pgAdmin + mock-log-producer only; **removed API (`log-analyzer`) service** from compose.
- **README:** Compose infra + AppHost API model.

### Phase 4 — Docs & configuration (2026-05-10)

- Runbook in README; **`UserSecretsId`** on **`LogAnalyzer.Api`**; periodic analysis waits first timer tick before first Graylog cycle.

### Phase 3 — Operational hardening & mock traffic (2026-05)

- Outbound metrics, Jira health/readiness, dispatcher hardening; mock producer **`eval_*`** harness labels (not classification ground truth).

### Phase 2 — PostgreSQL, Graylog, OpenAI batch periodic (2026-05-01)

- EF schema, **`GraylogLogProvider`**, **`OpenAiLogAnalyzer`**, **`PeriodicLogAnalysisBackgroundService`**, **`GET /log-analysis`**.

### Phase 1 — Layering & API queue (2026-04-28)

- Solution split, bounded queue + **`429`**, AI retry/fallback, grouping.
