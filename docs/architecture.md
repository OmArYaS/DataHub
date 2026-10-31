# DataHub — architecture

> One reporting surface over **SQL Server / Azure SQL / MySQL / SQLite**
> sources: a *query catalog* that admin editors author once, users run per
> role, a Redis/memory cache absorbs the repeat reads, and periodic CSV
> partitions land in Blob Storage for the offline consumers. The demo profile
> runs everything locally on SQLite; production profiles swap providers.
>
> **Design bias:** the catalog is *code, not policy* — rules are enforced in
> compiled C# (`ReadOnlyQueryGuard`, `ReportAccess`, `KeyFormat`) with
> enumerated test suites, while the *secrets* never travel: the registry
> stores configuration keys, resolved to connection strings at call time.

```
┌──────────────┐   JWT (oid + roles)   ┌─────────────────────────────────────┐
│  React SPA   │◄─────────────────────►│              DataHub API            │
│ (Vite, 5173) │   CORS 5173/7200      │  Controllers · ETag/304 · Problem-  │
└──────────────┘                       │  Details envelope (code ext member) │
                                       └───────┬─────────────────────────────┘
                                               │ Application layer
   ┌───────────────────────────────────────────┼─────────────────────────────────┐
   │  ReportCatalogService  ReportExecutionService        SyncJobService         │
   │  SourceRegistryService SourceHealthOrchestrator      SyncSchedulerService  │
   └──────┬──────────────────────┬──────────────────────┬───────────────────────┘
          │ ICatalogDbContext    │ ISourceReadExecutor  │ IReportSink
          ▼                      ▼                      ▼
   ┌──────────────┐      ┌────────────────┐     ┌──────────────────┐
   │ Catalog DB   │      │ AdoSource-     │     │ Blob (datahub-   │
   │ EF Core      │      │ Executor:      │     │ sync) / local    │
   │ Sqlite/SqlS- │      │ SqlClient ·    │     │ var/sync folder  │
   │ erver        │      │ MySql-         │     │ (RFC 4180 CSV)   │
   └──────────────┘      │ Connector ·   │     └──────────────────┘
   source registry,      │ SqliteClient  │
   query catalog,        └────────────────┘
   sync Run ledger             ▲ ICacheStore → Redis (prod) / IDistributedCache (demo)
   demo warehouse              ▲ ISourceConnectionResolver → config slots
```

## 1. Layers and ownership

| Project | Owns | Never contains |
|---|---|---|
| `DataHub.Domain` | Entities (`DataSource`, `ReportDefinition`, `ReportGrant`, `SyncRun`), enums with lowercase text contract, and the **Rule classes**: `ReadOnlyQueryGuard`, `SqlDialect`, `CsvBuilder` | Any framework package |
| `DataHub.Application` | Services (catalog, execution, registry, sync), abstractions (`ICatalogDbContext`, `ICacheStore`, `ISourceReadExecutor`, `ISourceConnectionResolver`, `IReportSink`), DTO records, `FixedUser`/`SystemPrincipal` | ASP.NET, EF providers, Azure SDKs |
| `DataHub.Infrastructure` | EF Core catalog context + snake_case mapping + migration, `AdoSourceExecutor`, config-based secret resolver, cache wiring (Redis→memory), Blob + filesystem sinks, `CurrentUser` | Web controllers |
| `DataHub.Api` | Controllers, JWT auto-switching (Development/AzureAd), policies (`hub.admin`, `hub.analyst`), ProblemDetails middleware, health checks, host composition |

`Directory.Build.props` at the repo root pins every project to
`net10.0` / nullable / implicit usings / NETAnalyzers — *0 warnings is the
build quality gate* (the CI asserts it).

## 2. The three rules that carry the platform

### 2.1 ReadOnlyQueryGuard — the only SQL an editor can ever store

* must start with `SELECT` (single statement, no `;`),
* any SQL comment (`--`, `/*`) is rejected outright — comments hide
  keywords past any word list,
* a blocklist of write/DDL keywords (`insert`, `update`, `drop`, `exec`,
  `grant`, `vacuum`, …) matched with word boundaries *inside the whole
  statement* — `created_utc` survives, `vacuum as mode` does not,
* an explicit `ORDER BY` is mandatory: the dialect wrapper orders by the
  first output column so page 4 of today equals page 4 of tomorrow,
* > 4000 chars is rejected (the catalog stays reviewable).

Rejections keep their exact reason and surface as
`422 INVALID_QUERY` ProblemDetails. The tests enumerate the keyword list so
a drift is *caught, not guessed*.

### 2.2 SqlDialect — one catalog SELECT, three engine wrappers

```
rows: SELECT * FROM (catalog select) AS sub ORDER BY 1 LIMIT n OFFSET m   -- MySQL/SQLite
      SELECT * FROM (catalog select) AS sub ORDER BY 1 OFFSET m ROWS FETCH NEXT n ROWS ONLY  -- (Azure) SQL
count: SELECT COUNT(1) FROM (catalog select) AS sub
ping:  SELECT 1
```

Only validated integers ever meet the wrapper literals (invariant-culture
rendering) — user text is never concatenated.

### 2.3 ReportAccess — kits and grants

A report runs for admins, or any caller whose token carries at least one
role granted on the report (lowercase vocabulary: `viewer` / `analyst`
/`admin`, from the `roles` claim — raw types, `MapInboundClaims=false`).
Every path calls the same static engine → 403 `PERMISSION_DENIED` with the
exact role list in the detail.

## 3. Secrets: resolve at call time, never store

A `DataSource` row stores only `Sources:{ConnectionName}:ConnectionString`
(the configuration slot). `ConfiguredSourceConnectionResolver` turns the
slot into the live string at call time — in production the slot can hold a
Key Vault reference; *rotation is a config edit, never a migration.* DTOs
expose an `isConfigured` **bool** and never the string.

## 4. Caching: the same bytes from memory and Redis

`ICacheStore` (application) → `DistributedCacheStore` over `IDistributedCache`:
`Cache:Redis:ConnectionString` switches to `AddStackExchangeRedisCache`
(`datahub:` key prefix); absent it, the in-memory provider serves demo
profiles and contract tests. Keys are `rw:{reportKey}:{page}:{pageSize}` —
*roles gate entry to catalog data, not the payload*; one slot per page
shape. TTLs are per report (`cache_seconds_ttl`, clamped 5–3600 s).

Every fresh (non-cached) result carries a deterministic ETag:
`CacheStamp.Compute` sha-256s reportKey + page/size + count + columns + the
first 64 rows into 16 hex chars. The controller honours `If-None-Match`
with a real **304** on the same stamp — whether the payload came out of the
engine or the cache wall (which is why stamp tests run both directions).

## 5. Sync jobs (blob partitions)

`SyncJobService.RunAsync` writes one ledger row (`SyncRun`) → pulls the
table in bounded pages of 500 (the source-protection ceiling — the sync
path never bypasses it) → serializes CSV via `CsvBuilder` (RFC 4180:
header, quote-on-necessity, doubled quotes, CRLF) → writes the partition →
`completed` with row count. Failures are *data*: `failed` +
`failure_code` (`SOURCE_UNAVAILABLE`, `SOURCE_NOT_CONFIGURED`,
`BLOB_WRITE_FAILED`, `SYNC_ENGINE_ERROR`) — the scheduler retries by
cadence, the dashboard displays them.

Path layout (identical for Azure Blob and the demo filesystem sink):

```
datahub-sync/{reportKey}/{yyyy/MM/dd}/part-{HHmmss}-{nonce}.csv
```

`SyncDuePicker` is a pure cadence rule (window elapsed by the **latest run
of any status**, or never-run) feeding `SyncSchedulerService`
(`Sync:SchedulerEnabled`, `Sync:IntervalSeconds`; the contract tests disable
the background loop). `RunDueAsync` also has a manual admin endpoint.

## 6. Persistence profiles

| Profile | `Catalog:Provider` | Used where |
|---|---|---|
| Sqlite demo | `Sqlite` | Local dev, contract tests (temp-file db doubles as demo warehouse) |
| SQL Server | `SqlServer` | Production catalog store |
| Azure SQL | `AzureSql` | Production catalog store (same dialect) |

Snake_case tables (`data_source`, `report_definition`, `report_permission`,
`sync_run`) with a **lowercase enum text contract** for every status/role
column (`provider`, `status`, `role_name` — `EnumText.Of`). The SQLite
migration is hand-authored (`[Migration("0001_initial_create")]`) and
reviewed like code; host boot runs migrate + idempotent seed *before any
traffic*.

The demo local warehouse (`dim_region` + 72 `fact_revenue` rows, seeded
deterministically) rides on the same SQLite file that backs the catalog —
`local_warehouse` is therefore a real reachable source, while
`mysql_retail` and `azure_sql_finance` are registered-but-unreachable so
health reporting, 503 mapping, and failure ledger rows stay honest.

## 7. Identity modes (FinTrack/ModernForms pattern)

`Auth:Mode=Development` → the API issues local JWTs from configured demo
users (`nadia`/admin, `lucas`/analyst, `sara`/viewer) via
`POST /api/auth/token`. `Auth:Mode=AzureAd` → tokens are validated against
the tenant; no code differences downstream — same `oid`/`roles` claim
reading, same policies. Explicit roles-environment mapping is a config
edit, not a deploy.

## 8. Error envelope

One shape for the whole API: `ExceptionHandlingMiddleware` renders
ProblemDetails carrying the stable `code` extension member (e.g.
`PERMISSION_DENIED`, `INVALID_QUERY`, `SOURCE_UNAVAILABLE`,
`SOURCE_NOT_CONFIGURED`, `NOT_FOUND`, `CREDENTIALS_REJECTED`). The SPA
never parses strings; Postman asserts on `code`.

| Route | Verbs | Policies |
|---|---|---|
| `/api/auth/token` | POST | anonymous (demo issuer only) |
| `/api/reports`, `/api/reports/{key}` | GET | authenticated (grant-filtered) |
| `/api/reports` | POST | `hub.admin` |
| `/api/reports/{key}/query` | POST | authenticated + `ReportAccess` |
| `/api/sources` | GET | authenticated |
| `/api/sources` | POST | `hub.admin` |
| `/api/sources/{key}/test\|disable\|enable` | POST | `hub.admin` |
| `/api/sync/run/{key}`, `/api/sync/run-due` | POST | `hub.admin` |
| `/api/sync/runs`, `/api/sync/dashboard` | GET | `hub.analyst` (admins ride along) |
| `/health` | GET | ready-tag checks only (catalog-db; Redis when bound) |
| `/health/all` | GET | every check; federated sources degrade, never gate |

## 9. External-engine integration

`database/mysql_storefront.sql` + `database/sqlserver_warehouse.sql` seed
the two service containers. Connection strings reach the executor tests via
the `DATAHUB_MYSQL_CONN` / `DATAHUB_SQLSERVER_CONN` environment variables
(set in `pipelines/azure-pipelines.yml`); without them the tests skip
silently — a laptop run is never engine-hostage.

## 10. Testing posture

* **Unit** — `ReadOnlyQueryGuard`, `SqlDialect`, `CsvBuilder`, grant matrix,
  stamp determinism, due-picker, retry pipeline (transient blips retried ×2,
  non-transient failing loud), registry battery (masking, slots, pings).
* **Contract** — real `WebApplicationFactory` hosts per test: migrate+seed
  boot path, JWT flows, policy gates, cache MISS→HIT, ETag ↔ 304, 422/400/404
  code paths, sync ledger + partition bytes, health ready/degraded.
* **Integration** — live MySQL + SQL Server through the same
  `AdoSourceExecutor` the API uses, env-gated.

The suite runs green with 0 warnings (`CA1707` waived only for test names).
