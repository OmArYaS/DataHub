# DataHub

> One reporting surface over SQL Server / MySQL / SQLite sources —
> an admin-authored read-only query catalog, per-role grants, Redis-or-memory
> caching with honest ETags/304s, and periodic CSV partitions synced to Blob
> Storage. ASP.NET Core 10 (EF Core, full analyzers at **0 warnings**) +
> React (Vite) + a CI pipeline running the test suite.

## Why this project exists (interview brief)

Data teams drown in *one-off SQL*: every ad-hoc dashboard is a secrets-leak
risk with a paging bug. DataHub is the alternative:

- **Editors register, users run.** Admins publish SELECTs into a catalog
  protected by `ReadOnlyQueryGuard` (statement shape, keyword blocklist,
  mandatory `ORDER BY`). Users run published reports; they never submit SQL.
- **Rules are code.** The grant matrix (`ReportAccess`), key syntax
  (`KeyFormat`), and dialect wrapping (`SqlDialect`) are compiled classes
  with enumerated xUnit suites — policy you can't drift.
- **Secrets never store.** Source rows keep only configuration keys; live
  connection strings resolve per call (`Sources:{ConnectionName}:Connection
  String`) — a Key Vault rotation is a config edit, never a migration.
- **A cache that admits its sins.** Every result carries a deterministic
  ETag computed over the table; a matching `If-None-Match` gets a real 304,
  whether the payload came from the engine or the cache. Cache hits are
  labelled `fromCache`.
- **Sync jobs are ledgered.** Every Blob partition run (cadence-driven,
  manual, or scheduler-triggered) is one `SyncRun` row — completed/failed,
  row counts, failure codes — surfaced by the dashboard's tiles.

## Quickstart (demo profile — everything local, SQLite)

```bash
dotnet run --project src/DataHub.Api
# → http://localhost:7200 (migrates + seeds before any traffic)
#   swagger: http://localhost:7200/swagger
```

Demo users (Development issuer): `nadia` (admin), `lucas` (analyst),
`sara` (viewer).

```http
POST /api/auth/token                          {"userName":"nadia"} → Bearer token
POST /api/reports/revenue_by_region/query                          → rows + ETag
POST /api/sources/local_warehouse/test                             → ping battery
POST /api/sync/run/revenue_by_region                               → CSV partition + ledger row
GET  /health      · /health/all                                    → readiness / honest degradation
```

## The webapp

`webapp/` — Vite + React SPA: login gate, KPI dashboard, report runner with
page controls, cache badge + 304 handling, admin source tests, sync ledger.
Dev: `cd webapp && npm install && npm run dev` (proxies `/api` to :7200).
CI compiles it; local builds need Node 20+.

## Repo map

```
src/
  DataHub.Domain/          entities, enums (lowercase text contract), rules
  DataHub.Application/     services + abstractions + DTOs (framework-free)
  DataHub.Infrastructure/  EF Core catalog + migration, source executors, cache, sinks
  DataHub.Api/             controllers, JWT/policies, health, middleware
tests/DataHub.Tests/       unit rules + retry contracts + API contracts + live-engine (env-gated)
webapp/                    React SPA (Vite)
database/                  external-engine seed scripts (CI service containers)
postman/                   ready-to-import collection (codes asserted in scripts)
pipelines/azure-pipelines.yml
docs/architecture.md       the whole design story, sectioned
```

## Testing

```bash
dotnet test DataHub.sln
# × unit suites: guard, dialects, CSV, grants, stamps, due-picker, retry line
# × registry battery: masking, config slots, ping verdicts
# × API contracts (real WebApplicationFactory hosts): auth, role filtering,
#   cache MISS→HIT, ETag→304, PERMISSION_DENIED, sync ledger, health probes
```

Live-engine integration (MySQL + SQL Server through the same
`AdoSourceExecutor` the API uses) runs when `DATAHUB_MYSQL_CONN` /
`DATAHUB_SQLSERVER_CONN` are set — the CI pipeline attaches service
containers; a laptop run simply skips those tests.

## Backend stack

ASP.NET Core 10 (API) · EF Core 10 (catalog store; SQLite demo, SQL Server
production profile) · Microsoft.Data.SqlClient / MySqlConnector /
Microsoft.Data.Sqlite (federated reads) · `IDistributedCache` → Redis in
production (`datahub:` key prefix), in-memory in demos/tests ·
Azure.Storage.Blobs (sync sink; folder-backed demo) · Swashbuckle ·
AspNetCore.HealthChecks (+ Redis) · xUnit.

## Assumptions & notes

- The demo signing key and demo users live in `appsettings.json` — committed
  on purpose (it is a local-only Development issuer). Production binds
  `Auth:Mode=AzureAd` and every secret moves to Key Vault slots. The shipped
  MySQL / remote SQL credentials are fake by intent: they keep the demo
  health reporting honest (reachable local warehouse vs unreachable clouds).
- Sync partitions cap at 50 000 rows, pulled in pages of 500 — the same
  source-protection ceiling the API path enforces. One honest ceiling.
- The webapp ships without a lockfile; the pipeline installs with
  `npm install --no-audit --no-fund` (consistent with the other portfolio
  repos' pipeline contract).
