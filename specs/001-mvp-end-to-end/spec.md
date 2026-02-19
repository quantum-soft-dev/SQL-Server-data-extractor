# Feature Specification: MVP End-to-End CDC Data Extractor

**Feature Branch**: `001-mvp-end-to-end`
**Created**: 2026-02-15
**Status**: Draft
**Input**: Phase 1 scope from `docs/PHASE-1.md` with full PRD context

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Initial Setup & First Snapshot (Priority: P1)

A data engineer installs the Windows application and goes through
a guided setup wizard to connect to SQL Server, authorize the
downstream service, select tables for extraction, configure CDC
and retention policies, set a schedule, and run the first full
snapshot of all selected tables.

The wizard consists of 9 steps:
1. **Welcome / Environment** — system checks (OS, .NET runtime,
   service registration availability).
2. **Connect to SQL Server** — enter server/instance/database,
   choose authentication (Windows/AD or SQL Login), test
   connection, verify prerequisites (SQL Agent running,
   permissions, CDC status).
3. **Downstream Authorization** — OAuth Device Flow: request
   device code, user authorizes in browser, poll for token,
   store refresh token securely.
4. **Discover & Select Tables** — browse available tables with
   search/filter, see CDC readiness, unique key status, estimated
   row count, and extraction mode (CDC / SNAP / BLOCKED). Select
   tables via checkboxes.
5. **CDC & Retention Policy** — toggle auto-enable CDC on database
   and tables, set minimum retention (default 7 days), configure
   batch inactivity TTL (default 10 min), enable small-table
   snapshot fallback with row threshold (default 1000).
6. **Schedule** — configure run times (default 08:00, 12:00,
   18:00 local), with ability to add/remove times, prevent
   parallel runs (hard rule).
7. **Review & Apply** — summary of all actions, apply button with
   progress bar, results checklist (service installed, CDC
   enabled per table, retention set).
8. **Bootstrap Run** (optional) — run the initial SNAPSHOT batch
   immediately with per-table progress and live logs.
9. **Done** — summary of completed setup, link to open management
   console.

**Why this priority**: Without initial setup and first snapshot,
no data flows. This is the foundation for everything else.

**Independent Test**: Install the app on a clean Windows machine,
complete the wizard against a test SQL Server database with 3-5
tables, verify SNAPSHOT batch succeeds and data appears in
downstream.

**Acceptance Scenarios**:

1. **Given** a clean Windows 10/11 machine with .NET 8+ runtime,
   **When** the user launches the application for the first time,
   **Then** the configurator wizard opens at the Welcome step and
   displays environment check results (OS, runtime, service
   registration).

2. **Given** the user is on the Connect step with valid SQL Server
   credentials, **When** they click "Test Connection", **Then** the
   app verifies connectivity and displays prerequisites status
   (SQL Agent, permissions, CDC on database).

3. **Given** the user is on the Downstream Authorization step,
   **When** they request a device code and complete browser
   authorization, **Then** the app receives tokens and stores
   the refresh token securely via DPAPI.

4. **Given** the user is on the Discover & Select Tables step,
   **When** tables are loaded, **Then** each table shows its
   schema, name, CDC status, unique key presence, estimated rows,
   and mode (CDC/SNAP/BLOCKED with reason).

5. **Given** a table without a unique key and more than 1000 rows,
   **When** the user tries to select it, **Then** the table is
   shown as BLOCKED with explanation: "No unique index. Add a
   unique index or reduce rows below threshold for SNAP mode."

6. **Given** the user has completed all wizard steps and clicks
   "Apply Configuration", **When** the apply process runs,
   **Then** the system installs the Windows Service, enables CDC
   on the database and selected tables, raises retention to the
   configured minimum (without lowering existing higher values),
   and saves configuration and state store.

7. **Given** the user chooses "Run Snapshot Now" on step 8,
   **When** the SNAPSHOT batch runs, **Then** per-table progress
   is displayed with percentage, live logs stream in real-time,
   and each table is sent to downstream as CSV chunks with a
   schema manifest, followed by dataset commit.

8. **Given** the SNAPSHOT batch completes, **When** the user
   reaches the Done step, **Then** the summary shows service
   status (running), CDC enabled count, snapshot result
   (SUCCEEDED/FAILED), and next scheduled run time.

---

### User Story 2 - Incremental Delta Extraction (Priority: P2)

After the initial snapshot, the Windows Service automatically runs
on the configured schedule (default: 08:00, 12:00, 18:00). Each
run creates a DELTA batch that extracts only CDC changes (inserts,
updates, deletes) since the last successful extraction, sends them
to downstream as CSV chunks with service columns (`_op`, `_lsn`,
`_seqval`, `_ts`), and advances the last-processed LSN only after
successful dataset commit.

The service:
- Reads `last_processed_lsn` per table from state store.
- Computes `to_lsn` (current maximum LSN).
- Reads CDC changes in the range `(last_processed_lsn, to_lsn]`.
- Creates a batch in downstream, uploads dataset chunks (gzip CSV,
  respecting MaxBytes), commits each dataset, then finishes batch.
- Sends heartbeat every 120 seconds while batch is active.
- Advances `last_processed_lsn` ONLY after successful commit.

For small tables in SNAP mode (no unique key, below threshold):
the service performs a full snapshot on every run instead of CDC.

**Why this priority**: Incremental extraction is the core value
proposition. Without deltas, the system is just a one-time bulk
loader.

**Independent Test**: After a successful SNAPSHOT, insert/update/
delete rows in the source database, wait for or trigger a
scheduled run, verify the DELTA batch contains exactly the
expected changes with correct `_op` values.

**Acceptance Scenarios**:

1. **Given** a table with a completed SNAPSHOT and a stored
   `last_processed_lsn`, **When** the scheduled run triggers,
   **Then** the service reads only CDC changes after that LSN
   and sends them as a DELTA batch.

2. **Given** 3 inserts, 2 updates, and 1 delete occurred in a
   table since the last run, **When** the DELTA batch processes
   that table, **Then** the CSV chunks contain exactly 6 rows
   with `_op` values I, I, I, U, U, D respectively, plus `_lsn`,
   `_seqval`, and `_ts` columns.

3. **Given** a DELTA batch is running, **When** the dataset for
   a table is committed to downstream, **Then** the service
   advances `last_processed_lsn` for that table to `to_lsn`.

4. **Given** a DELTA batch is running, **When** the dataset commit
   fails, **Then** the service does NOT advance
   `last_processed_lsn` and logs the error with table name, LSN
   range, and failure reason.

5. **Given** a batch is active, **When** no activity occurs for
   more than 120 seconds, **Then** the service sends a heartbeat
   to downstream to prevent batch TTL expiration.

6. **Given** a previous batch was not properly finished
   (e.g., service crashed), **When** a new batch starts, **Then**
   the new batch supersedes the old one via lease/fence token,
   and the old batch cannot commit.

7. **Given** a SNAP-mode table (no unique key, <1000 rows),
   **When** the scheduled run triggers, **Then** the service
   performs a full snapshot of that table (not CDC delta).

8. **Given** a scheduled run is already in progress, **When**
   another scheduled run would start, **Then** the second run
   is blocked (single-instance lock) and does not execute.

---

### User Story 3 - Monitoring & Troubleshooting via Management Console (Priority: P3)

The data engineer or operator opens the management console (same
Windows application, Manager mode) to monitor the service health,
view batch history, inspect per-table status, check diagnostics,
and read live logs. The manager connects to the running Windows
Service via IPC (Named Pipes + JSON-RPC).

Management console screens:
1. **Dashboard** — service status, last run summary, duration,
   CDC lag, current activity with per-table progress, diagnostics
   overview, recent runs list, and live logs.
2. **Runs** — filterable list of all batches (type, status,
   trigger, duration, tables, rows). Click to open run details.
3. **Run Details** — per-table datasets (LSN range, rows, chunks,
   status), errors reported to downstream and locally, and
   filtered logs for that specific run.
4. **Tables** — all enabled tables with mode, CDC status, last
   LSN, last sync time, lag, status, and re-init action.
5. **Diagnostics** — SQL Server checks (connectivity, SQL Agent,
   CDC enablement, capture/cleanup jobs, retention), permission
   checks, downstream service checks (HTTPS, tokens, API
   version), IPC/service checks (pipe, PID, uptime, state store).
6. **Settings** — view/edit schedule, CDC & retention parameters,
   SQL Server connection, downstream API settings, paths.
7. **Logs** — full live log stream with severity filter
   (INFO/WARN/ERROR), search, auto-scroll, pause, copy, and
   export.

**Why this priority**: Operators need visibility into what the
service is doing. Without monitoring, troubleshooting is
impossible.

**Independent Test**: Start the service with a known
configuration, open the management console, verify all screens
display correct data, trigger a run and observe live progress.

**Acceptance Scenarios**:

1. **Given** the Windows Service is running, **When** the user
   opens the management console, **Then** the app connects via
   IPC and the Dashboard shows: service status (Running), last
   run result, duration, CDC lag, and current activity.

2. **Given** 5 batches have completed (mix of SNAPSHOT, DELTA,
   SUCCEEDED, FAILED), **When** the user opens the Runs screen,
   **Then** all 5 batches are listed with correct type, status,
   trigger, duration, table count, and row count.

3. **Given** a FAILED batch, **When** the user opens Run Details,
   **Then** each table's dataset shows status (COMMITTED/ERROR/
   SKIPPED), errors show error code and human-readable message,
   and filtered logs for that run are available.

4. **Given** a table with a permission error, **When** the user
   opens the Tables screen, **Then** the table shows status
   "PERM ERR" with an actionable message (e.g., "Grant SELECT on
   cdc.dbo_Products_CT to user").

5. **Given** the service is actively running a batch, **When**
   the user opens the Dashboard, **Then** current activity shows
   per-table progress bars updating in real-time.

6. **Given** the user is on the Logs screen, **When** log entries
   stream in, **Then** entries are color-coded by severity (green
   for INFO, yellow for WARN, red for ERROR) and the user can
   filter, search, pause, copy, and export logs.

7. **Given** the user navigates to Diagnostics, **When** the
   checks are performed, **Then** each check shows OK/FAIL
   status with specific details (e.g., SQL Server version,
   retention value, token expiry time, pipe connection state).

---

### User Story 4 - CDC Gap Detection & Re-bootstrap (Priority: P4)

When the service detects that CDC history has been cleaned up
before it could extract changes (i.e., `last_processed_lsn` is
less than the minimum available LSN in CDC tables), it MUST NOT
silently continue. Instead, it flags the affected table for
re-bootstrap, reports the error to downstream and in local logs,
and displays the issue in the management console with an
actionable recommendation.

On the next SNAPSHOT batch, the flagged table is re-bootstrapped
(full snapshot replaces incremental extraction until a new LSN
baseline is established).

**Why this priority**: CDC gap is a data integrity issue. Silent
data loss is unacceptable per the project constitution.

**Independent Test**: Manually reduce CDC retention to force
cleanup of change tables, then trigger a run and verify the
system detects the gap, does not extract partial data, and flags
the table for re-bootstrap.

**Acceptance Scenarios**:

1. **Given** a table where `last_processed_lsn` is older than the
   minimum available CDC LSN, **When** the DELTA batch processes
   that table, **Then** the service detects the gap and does NOT
   extract partial changes.

2. **Given** a CDC gap is detected, **When** the error is
   reported, **Then** the service logs an Error with: table name,
   expected LSN, minimum available LSN, and recommendation
   (increase retention or run frequency).

3. **Given** a CDC gap is detected, **When** the batch error is
   reported to downstream, **Then** the error includes code
   `CDC_GAP_DETECTED`, scope `TABLE`, the affected table name,
   and `is_terminal: true` for that table.

4. **Given** a table is flagged for re-bootstrap, **When** the
   user opens the Tables screen in the management console,
   **Then** the table shows status "RE-BOOTSTRAP" with a message
   explaining the CDC gap and next action.

5. **Given** a table is flagged for re-bootstrap, **When** the
   next SNAPSHOT batch runs, **Then** the table is fully
   re-extracted (snapshot), a new `last_processed_lsn` baseline
   is established, and subsequent DELTA batches resume normally.

---

### User Story 5 - Manual Run Trigger (Priority: P5)

The operator can trigger a batch run immediately from the
management console without waiting for the next scheduled time.
The manual run follows the same flow as a scheduled run (creates
batch, processes all tables, commits datasets, finishes batch).
The single-instance lock prevents conflicts with scheduled runs.

**Why this priority**: Operators need the ability to run
extractions on demand for testing, after data fixes, or when
schedules change.

**Independent Test**: Open the management console, click "Run
Now", verify a batch starts immediately, runs to completion, and
results appear in the Runs list.

**Acceptance Scenarios**:

1. **Given** no batch is currently running, **When** the user
   clicks "Run Now" in the management console, **Then** a new
   batch starts immediately with trigger type `MANUAL`.

2. **Given** a batch is currently running, **When** the user
   clicks "Run Now", **Then** the button is disabled or the UI
   shows a message that a run is already in progress.

3. **Given** a manual run is triggered, **When** the batch
   completes, **Then** it appears in the Runs list with trigger
   `MANUAL` and all standard fields (type, status, duration,
   tables, rows).

---

### Edge Cases

- What happens when SQL Server is unreachable during a scheduled
  run? The service logs an Error with connection details, reports
  the error to downstream (if reachable), and retries according
  to the transient fault policy. If all retries fail, the batch
  is finished as FAILED.

- What happens when the downstream service is unreachable during
  chunk upload? The service retries with exponential backoff for
  transient errors (429, 503, network timeout). After exhausting
  retries, the dataset is aborted and the batch is finished as
  FAILED. `last_processed_lsn` is NOT advanced.

- What happens when the service crashes mid-batch? On restart,
  the service does not resume the old batch. The next scheduled
  (or manual) run creates a new batch. The old batch in
  downstream is superseded via lease/fence token (or TTL
  expiration after 10 minutes of inactivity).

- What happens when a table's schema changes (ALTER TABLE)
  between runs? Out of scope for MVP (Phase 3). In MVP, the
  service continues with the existing capture instance. Schema
  evolution detection is deferred.

- What happens when CDC is disabled on a table by a DBA between
  runs? The service detects this during the run (CDC check fails),
  logs an Error, reports to downstream, skips the table, and
  continues with other tables. The table shows an error status
  in the management console.

- What happens when the OAuth refresh token expires or is
  revoked? The service logs an Error (auth failure), the batch
  fails, and the management console shows "Re-authorize" in
  Diagnostics and Settings.

- What happens when a chunk exceeds MaxBytes? The service splits
  data into chunks that respect the MaxBytes limit. If a single
  row exceeds MaxBytes, the service logs an Error for that table.

## Requirements *(mandatory)*

### Functional Requirements

**Service & Scheduling**

- **FR-001**: System MUST operate as a Windows Service (long-lived
  process) that executes extraction jobs on a configurable
  schedule.
- **FR-002**: System MUST support configurable run times (default:
  08:00, 12:00, 18:00 local server time) with the ability to add
  and remove times via the management console.
- **FR-003**: System MUST enforce single-instance lock: parallel
  runs are forbidden. A scheduled run MUST NOT start while another
  run is in progress.
- **FR-004**: System MUST support manual run trigger via IPC from
  the management console.

**CDC Management**

- **FR-005**: System MUST verify prerequisites before extraction:
  SQL Server connectivity, SQL Agent running, sufficient
  permissions (db_owner or cdc_admin, SELECT on sys.tables,
  SELECT on CDC tables, EXECUTE on CDC functions).
- **FR-006**: System MUST auto-enable CDC on the database and on
  selected tables (when permitted by configuration).
- **FR-007**: System MUST configure CDC retention to at least the
  configured minimum (default 7 days). The system MUST NOT lower
  retention if the server already has a higher value.
- **FR-008**: System MUST verify that SQL Agent CDC capture and
  cleanup jobs exist and are operational.

**Snapshot Extraction**

- **FR-009**: System MUST perform a full SNAPSHOT extraction of all
  selected tables on initial setup (or re-bootstrap).
- **FR-010**: System MUST send snapshot data as gzip-compressed CSV
  chunks to downstream, respecting the MaxBytes limit per chunk.
- **FR-011**: System MUST generate and upload a JSON schema manifest
  per table containing: table name, columns (name, SQL type,
  nullable, length/precision/scale), primary key, and schema hash.
- **FR-012**: System MUST create a batch in downstream (type
  `SNAPSHOT`), create a dataset per table, upload chunks, commit
  each dataset, and finish the batch.

**Delta Extraction**

- **FR-013**: System MUST read CDC changes in the LSN range
  `(last_processed_lsn, current_max_lsn]` per table.
- **FR-014**: System MUST include service columns in delta CSV:
  `_op` (I/U/D), `_lsn`, `_seqval`, `_ts`.
- **FR-015**: System MUST create a DELTA batch in downstream,
  upload dataset chunks, and commit per table.
- **FR-016**: System MUST advance `last_processed_lsn` ONLY after
  successful dataset commit for the corresponding table.

**Small-Table Fallback**

- **FR-017**: For tables without a unique key and estimated row
  count at or below the configured threshold (default 1000),
  system MUST support SNAP mode: full snapshot on every run.
- **FR-018**: For tables without a unique key and estimated row
  count above the threshold, system MUST block selection and
  display the reason in the UI.

**Downstream API Integration**

- **FR-019**: System MUST implement the batch lifecycle:
  create batch, heartbeat (every 120 seconds), finish batch
  (SUCCEEDED/FAILED/ABORTED).
- **FR-020**: System MUST implement the dataset lifecycle:
  create dataset, upload chunks, commit/abort dataset.
- **FR-021**: System MUST implement batch fencing: a new batch
  supersedes any previous active batch via lease/fence token.
- **FR-022**: System MUST upload schema manifests via
  `PUT /tables/{table}/schemas/{schema_hash}` (idempotent by
  schema hash). Schema is sent at initial snapshot and when
  schema changes are detected.
- **FR-023**: All chunk uploads and dataset commits MUST be
  idempotent using deterministic keys:
  `(table, from_lsn, to_lsn, chunk_no)`.
- **FR-024**: System MUST report errors to downstream via the
  batch errors endpoint with: scope, table, severity, stable
  error code, human-readable message, and retryability flag.

**CDC Gap Detection**

- **FR-025**: System MUST detect when `last_processed_lsn` is less
  than the minimum available CDC LSN for a table.
- **FR-026**: On gap detection, system MUST NOT extract partial
  data. It MUST flag the table for re-bootstrap, log an Error,
  and report to downstream.
- **FR-027**: On the next SNAPSHOT batch, flagged tables MUST be
  fully re-extracted to establish a new LSN baseline.

**State Management**

- **FR-028**: System MUST persist per-table state:
  `last_processed_lsn`, bootstrap status (pending/complete/
  re-bootstrap), and schema hash.
- **FR-029**: State store MUST survive service restarts without
  data loss.

**IPC (Manager <-> Service)**

- **FR-030**: System MUST expose an IPC endpoint via Windows Named
  Pipes using JSON-RPC (StreamJsonRpc).
- **FR-031**: IPC MUST support: get service status, get batch
  progress (per-table), trigger manual run, subscribe to live log
  stream, get diagnostics results.
- **FR-032**: Named Pipe MUST restrict access via ACL to authorized
  Windows accounts/groups only.

**Windows Application (Configurator + Manager)**

- **FR-033**: Application MUST provide a 9-step configurator wizard
  for initial setup (as described in User Story 1).
- **FR-034**: Application MUST provide a management console with 7
  screens: Dashboard, Runs, Run Details, Tables, Diagnostics,
  Settings, Logs (as described in User Story 3).
- **FR-035**: Application UI MUST follow the design system defined
  in `docs/designs/ui/` (colors, typography, spacing, component
  library, layout grid).
- **FR-036**: Application MUST display errors with human-readable
  messages and actionable recommendations (e.g., "Grant SELECT on
  cdc.dbo_Products_CT" rather than raw exception text).

**Error Handling & Retry**

- **FR-037**: System MUST retry transient errors (network timeouts,
  SQL deadlocks, HTTP 429/503) with exponential backoff.
- **FR-038**: System MUST classify errors as table-level (skip
  table, continue batch) vs. batch-level (stop entire batch).
- **FR-039**: System MUST report batch status as SUCCEEDED (all
  tables committed), FAILED (one or more tables failed), or
  ABORTED (batch terminated prematurely).

**Observability**

- **FR-040**: System MUST produce structured JSON logs with
  correlation ID per batch, table name, LSN range, and row counts.
- **FR-041**: Log messages MUST be readable and informative without
  requiring source code knowledge (per constitution Principle IV).
- **FR-042**: System MUST write Critical and Error events to
  Windows Event Log.
- **FR-043**: System MUST stream logs in real-time to the
  management console via IPC subscription.

**Security**

- **FR-044**: System MUST store OAuth tokens securely via DPAPI
  (Windows Credential Manager).
- **FR-045**: System MUST NOT store passwords or tokens in plain
  text in configuration files.
- **FR-046**: SQL Server connections MUST support TLS
  (`Encrypt=True`).
- **FR-047**: System MUST support both Windows/AD authentication
  and SQL Login for SQL Server connectivity.

### Key Entities

- **Batch**: A single extraction run (SNAPSHOT or DELTA). Has an
  ID, type, trigger (SCHEDULED/MANUAL), status, start/end time,
  and a collection of datasets. Corresponds to a batch in
  downstream.

- **Dataset**: Extraction of one table within a batch. Identified
  by table + LSN range. Contains chunks. Status lifecycle:
  CREATED -> UPLOADING -> COMMITTED / ABORTED.

- **Chunk**: A portion of CSV data for a dataset. Identified by
  chunk number. Gzip-compressed. Respects MaxBytes limit.

- **TableState**: Per-table persistent state: table identifier
  (schema.table), extraction mode (CDC/SNAP), `last_processed_lsn`,
  bootstrap status (PENDING/COMPLETE/RE_BOOTSTRAP), schema hash,
  last sync timestamp.

- **SchemaManifest**: JSON description of a table's structure:
  columns (name, type, nullable, precision), primary key, unique
  keys, schema hash. Sent to downstream and referenced by
  datasets.

- **Schedule**: Collection of run times (cron-like) with timezone.
  Controls when the service triggers extraction batches.

- **DiagnosticCheck**: A prerequisite or health check result:
  check name, status (OK/FAIL/WARN), detail message, remediation
  hint.

### Assumptions

- SQL Server 2016+ with CDC support (Enterprise, Standard, or
  Developer edition).
- Windows 10/11 or Windows Server 2016+ as the deployment target.
- .NET 8+ runtime is available on the target machine.
- Downstream service API follows the contract in `docs/APIs.md`.
- OAuth Device Flow is the authentication mechanism for downstream.
- A single SQL Server database is extracted per service instance
  (multi-source is out of scope).
- Schema evolution detection and automatic re-bootstrap on ALTER
  TABLE are deferred to Phase 3.
- `PARTIAL` batch status is not supported in MVP; batches are
  SUCCEEDED, FAILED, or ABORTED.
- Replica mode (applying changes into an up-to-date state in
  downstream) is out of scope.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with no prior experience can complete the
  setup wizard and run the first SNAPSHOT within 15 minutes on a
  test database with 5 tables.

- **SC-002**: After initial SNAPSHOT, incremental DELTA extraction
  correctly captures 100% of CDC changes (inserts, updates,
  deletes) with zero data loss when the service runs within the
  configured retention window.

- **SC-003**: The system detects CDC gaps (stale LSN vs. available
  CDC range) in 100% of cases and never silently skips data.

- **SC-004**: After a service crash or unexpected restart, the next
  run resumes correctly: no LSN gaps, no duplicate dataset commits
  (idempotency), and the batch completes successfully.

- **SC-005**: The management console displays current batch
  progress (per-table) with updates at least every 5 seconds when
  connected via IPC.

- **SC-006**: Every error in the system is logged with sufficient
  context (table name, LSN range, batch ID, error code) that an
  operator can diagnose the issue without reading source code.

- **SC-007**: All log messages, error messages, and UI status texts
  are actionable: they describe what happened, what is affected,
  and what the user can do to resolve the issue.

- **SC-008**: The system can extract and upload a 1-million-row
  table snapshot within the configured schedule window (time
  between two scheduled runs, e.g., 4 hours).
