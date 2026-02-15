# Research: MVP End-to-End CDC Data Extractor

**Branch**: `001-mvp-end-to-end` | **Date**: 2026-02-15

## R-001: Snapshot + CDC Consistency Strategy

**Decision**: Use "LSN Boundary with SNAPSHOT Isolation" algorithm.

**Algorithm (step-by-step)**:
1. Enable `ALLOW_SNAPSHOT_ISOLATION ON` on the database (one-time,
   during CDC setup).
2. Begin transaction with `IsolationLevel.Snapshot`.
3. Capture `snapshot_max_lsn = sys.fn_cdc_get_max_lsn()` BEFORE
   reading any table data.
4. Read all selected tables within the same SNAPSHOT transaction
   (ensures multi-table consistency — all tables see the same
   point-in-time state).
5. Commit the transaction.
6. Upload snapshot data to downstream (outside DB transaction).
7. After successful downstream commit, store `snapshot_max_lsn`
   as `last_processed_lsn` for each table.

**First CDC read after snapshot**:
- `from_lsn = sys.fn_cdc_increment_lsn(last_processed_lsn)`
- `to_lsn = sys.fn_cdc_get_max_lsn()`
- Read via `cdc.fn_cdc_get_all_changes_<capture_instance>(
    @from_lsn, @to_lsn, N'all update old')`.

**Rationale**:
- Zero data loss: changes committed during snapshot have LSN >
  `snapshot_max_lsn` and are captured by the next CDC read.
- Zero duplicates: `sys.fn_cdc_increment_lsn()` ensures the
  boundary is exclusive.
- Multi-table consistency: one SNAPSHOT transaction for all tables.
- Battle-tested: same approach used by Debezium and SSIS CDC.

**Alternatives considered**:
- "Snapshot first, get LSN after" — REJECTED: creates a gap
  between snapshot start and LSN capture where changes can be lost.
- READ COMMITTED SNAPSHOT — REJECTED: provides only
  statement-level consistency, not transaction-level. Each SELECT
  sees a different point in time, breaking multi-table consistency.

**Trade-offs**:
- Requires tempdb space for row versioning (acceptable).
- Long snapshots hold versions in tempdb (mitigated by chunked
  uploads and time limits).
- Requires `ALTER DATABASE ... SET ALLOW_SNAPSHOT_ISOLATION ON`
  permission (documented in prerequisites).

## R-002: State Store Technology (Dapper vs EF Core)

**Decision**: Use Dapper with `Microsoft.Data.SqlClient`.

**Rationale**:
- **Simplicity**: State operations are simple (read by key, upsert,
  list, delete). EF Core's change tracking, relationship
  navigation, and LINQ queryable add zero value.
- **Testability**: `IStateStore` interface with in-memory fake is
  trivial to implement. EF Core's in-memory provider has SQL Server
  feature gaps (no MERGE, limited transaction semantics).
- **Transaction control**: LSN advancement requires explicit
  transaction scope — read CDC, send to downstream (outside TX),
  update LSN only on success. Dapper's explicit `IDbTransaction`
  fits perfectly. EF Core's `SaveChanges` model fights this pattern.
- **Footprint**: Dapper is ~150 KB, one NuGet package. EF Core
  adds ~2-3 MB and multiple packages.
- **YAGNI**: EF Core's migration system is overkill for 5-10
  simple state tables that change rarely. Idempotent `CREATE TABLE
  IF NOT EXISTS` scripts suffice.

**Alternatives considered**:
- Entity Framework Core — REJECTED: adds complexity without
  benefit for this use case. Change tracking, migrations, and
  LINQ-to-SQL are unused capabilities.
- Raw ADO.NET (no micro-ORM) — considered but Dapper adds minimal
  overhead with significant convenience (auto-mapping, parameterized
  queries).

**Migration approach**: Idempotent SQL scripts executed at service
startup / install time. No separate migration tool needed.

## R-003: OAuth Device Flow Implementation

**Decision**: Use raw `HttpClient` with custom implementation
(not MSAL), since the downstream is NOT Azure AD.

**Rationale**:
- MSAL is optimized for Microsoft Entra ID (Azure AD). Our
  downstream service uses its own OAuth 2.0 provider, so MSAL
  would require fighting its assumptions.
- Device Flow (RFC 8628) is straightforward to implement:
  1. POST to `/device/authorize` → get `device_code`, `user_code`,
     `verification_uri`, `interval`, `expires_in`.
  2. Display `user_code` + `verification_uri` in WPF UI.
  3. Poll `/token` at `interval` seconds with `device_code`.
  4. On success, store `access_token` (in memory) and
     `refresh_token` (DPAPI-encrypted on disk).

**Token storage**:
- Use `System.Security.Cryptography.ProtectedData` (built-in):
  - `ProtectedData.Protect(tokenBytes, null, DataProtectionScope.CurrentUser)`
  - `ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser)`
- Store encrypted bytes as Base64 in a local file alongside config.

**Token refresh via DelegatingHandler**:
- Custom `TokenRefreshHandler : DelegatingHandler` registered in
  `IHttpClientFactory` pipeline.
- On each request: inject `Bearer` token from `ITokenProvider`.
- On 401 response: acquire `SemaphoreSlim` lock, refresh token via
  `/token` endpoint with `grant_type=refresh_token`, retry request.
- Proactive refresh: check `expires_at` before sending, refresh
  if <5 minutes remaining.

**Cancellation**: Use `CancellationTokenSource` with linked
timeout for the polling loop. User can cancel from WPF UI.

**Alternatives considered**:
- MSAL.NET (`Microsoft.Identity.Client`) — REJECTED for MVP:
  designed for Microsoft identity platform, adds dependency
  complexity for a custom OAuth provider.
- IdentityModel (Duende) — considered, but adds an unnecessary
  dependency for a simple flow.

## R-004: Scheduler Implementation

**Decision**: Use Cronos library for cron expression parsing with
a custom `BackgroundService`-based scheduler.

**Rationale**:
- The schedule configuration uses cron-like expressions (as shown
  in the schedule wizard UI design).
- Cronos is lightweight (~50 KB), has no dependencies, and supports
  6-field cron expressions.
- A custom `BackgroundService` that calculates the next run time
  and uses `Task.Delay` is simpler than pulling in Quartz.NET or
  Hangfire for this use case.
- Single-instance lock: a simple `SemaphoreSlim(1,1)` or
  `bool _isRunning` flag with `Interlocked.CompareExchange`.

**Alternatives considered**:
- Quartz.NET — REJECTED: heavyweight scheduler framework, overkill
  for 3-5 daily runs with single-instance lock.
- Hangfire — REJECTED: requires a persistence backend, designed for
  distributed job processing.
- Windows Task Scheduler — REJECTED: service already runs as a
  long-lived process, external scheduler adds operational complexity.

## R-005: CSV Generation & Gzip Compression

**Decision**: Use `CsvHelper` for CSV serialization and
`System.IO.Compression.GZipStream` for compression.

**Rationale**:
- CsvHelper is the de-facto standard for CSV in .NET. Handles
  RFC 4180 quoting/escaping, nullable types, custom type converters.
- GZipStream is built-in, no external dependency.
- Chunking strategy: serialize rows into a `MemoryStream`, flush
  to gzip when approaching MaxBytes limit, emit chunk.

**Alternatives considered**:
- Manual CSV writing — REJECTED: error-prone for edge cases
  (embedded commas, quotes, newlines in data).
- Sep (Nie.Sep) — considered for performance, but CsvHelper is
  more mature and well-documented.
