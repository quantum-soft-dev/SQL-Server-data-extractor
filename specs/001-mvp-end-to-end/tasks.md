# Tasks: MVP End-to-End CDC Data Extractor

**Input**: Design documents from `/specs/001-mvp-end-to-end/`
**Prerequisites**: plan.md (required) ✓, spec.md (required) ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Tests**: Included — TDD is NON-NEGOTIABLE per project constitution Principle I

**Organization**: Tasks grouped by user story (5 stories: P1–P5) to enable independent implementation and testing

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Exact file paths included in every task

## Path Conventions

- Multi-project .NET solution: `src/<ProjectName>/` and `tests/<ProjectName>/`

---

## Phase 1: Setup (Project Infrastructure)

**Purpose**: Create .NET 8 solution, all projects, dependencies, and build configuration

- [X] T001 Create .NET 8 solution file src/CdcExtractor.sln with all 6 source projects (CdcExtractor.Domain, CdcExtractor.Application, CdcExtractor.Infrastructure, CdcExtractor.Contracts, CdcExtractor.Service, CdcExtractor.App) and configure project references per Clean Architecture layers in plan.md
- [X] T002 Create test solution with all 5 test projects (CdcExtractor.Domain.Tests, CdcExtractor.Application.Tests, CdcExtractor.Infrastructure.Tests, CdcExtractor.Service.Tests, CdcExtractor.App.Tests) in tests/ with references to corresponding source projects
- [X] T003 [P] Create Directory.Build.props at repository root with shared settings: TargetFramework net8.0, LangVersion 12, Nullable enable, ImplicitUsings enable, TreatWarningsAsErrors true
- [X] T004 [P] Add NuGet dependencies per plan.md: Microsoft.Data.SqlClient + Dapper (Infrastructure), StreamJsonRpc (Service + Contracts), Serilog + Serilog.Sinks.File + Serilog.Sinks.EventLog (Service), Polly (Infrastructure), CsvHelper (Infrastructure), Cronos (Service), CommunityToolkit.Mvvm (App), xUnit + NSubstitute + FluentAssertions (all test projects)

---

## Phase 2: Foundational (Domain, Contracts, Core Infrastructure)

**Purpose**: Domain model, shared contracts, and state store that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Tests for Foundational Phase ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T005 [P] Write unit tests for Lsn value object (parse hex, from bytes, comparison, Empty sentinel, equality, ToString) in tests/CdcExtractor.Domain.Tests/ValueObjects/LsnTests.cs
- [X] T006 [P] Write unit tests for TableIdentifier value object (FullName, QuotedFullName, equality, case handling) in tests/CdcExtractor.Domain.Tests/ValueObjects/TableIdentifierTests.cs
- [X] T007 [P] Write unit tests for SchemaHash value object (compute from manifest, equality, deterministic output) in tests/CdcExtractor.Domain.Tests/ValueObjects/SchemaHashTests.cs
- [X] T008 [P] Write unit tests for TableState entity (state transitions: Pending→Complete, Complete→ReBootstrap, ReBootstrap→Complete) in tests/CdcExtractor.Domain.Tests/Entities/TableStateTests.cs
- [X] T009 [P] Write unit tests for BatchRun entity (add datasets, status transitions, finish with duration) in tests/CdcExtractor.Domain.Tests/Entities/BatchRunTests.cs

### Implementation for Foundational Phase

- [X] T010 [P] Implement all domain enums in src/CdcExtractor.Domain/Enums/: ExtractionMode.cs (Cdc, Snap), BootstrapStatus.cs (Pending, Complete, ReBootstrap), BatchType.cs (Snapshot, Delta), BatchTrigger.cs (Scheduled, Manual), BatchStatus.cs (Running, Succeeded, Failed, Aborted), DatasetStatus.cs (Created, Uploading, Committed, Aborted, Skipped)
- [X] T011 [P] Implement Lsn value object (IComparable\<Lsn\>, IEquatable\<Lsn\>, Parse hex string, From byte array, Empty sentinel, hex ToString) in src/CdcExtractor.Domain/ValueObjects/Lsn.cs
- [X] T012 [P] Implement TableIdentifier value object (Schema, Name, FullName computed "schema.table", QuotedFullName computed "[schema].[table]", IEquatable) in src/CdcExtractor.Domain/ValueObjects/TableIdentifier.cs
- [X] T013 [P] Implement SchemaHash value object (SHA-256 hex digest of canonical JSON, IEquatable, Compute factory method) in src/CdcExtractor.Domain/ValueObjects/SchemaHash.cs
- [X] T014 [P] Implement BatchId value object (Guid wrapper, New factory, Parse factory) in src/CdcExtractor.Domain/ValueObjects/BatchId.cs
- [X] T015 [P] Implement DatasetId value object (Guid wrapper, New factory, Parse factory) in src/CdcExtractor.Domain/ValueObjects/DatasetId.cs
- [X] T016 [P] Implement TableState entity with state transition methods (MarkComplete, MarkReBootstrap) and validation per data-model.md in src/CdcExtractor.Domain/Entities/TableState.cs
- [X] T017 [P] Implement BatchRun entity with Datasets collection, AddDataset, Finish method, and status management in src/CdcExtractor.Domain/Entities/BatchRun.cs
- [X] T018 [P] Implement DatasetRun entity with status lifecycle (Created→Uploading→Committed/Aborted/Skipped) in src/CdcExtractor.Domain/Entities/DatasetRun.cs
- [X] T019 [P] Implement SchemaManifest and ColumnInfo as records per data-model.md in src/CdcExtractor.Domain/Entities/SchemaManifest.cs
- [X] T020 [P] Define all domain interfaces per data-model.md method signatures in src/CdcExtractor.Domain/Interfaces/: IStateStore.cs, IBatchHistoryStore.cs, IDownstreamClient.cs, ICdcReader.cs, ICdcManager.cs, ISchemaInspector.cs, IDiagnosticsService.cs, ITokenProvider.cs, IScheduler.cs
- [X] T021 [P] Implement domain exceptions with contextual properties (table name, LSN range, error message) in src/CdcExtractor.Domain/Exceptions/: CdcGapException.cs, SinkUploadException.cs, PrerequisiteCheckFailedException.cs
- [X] T022 [P] Implement domain events as records in src/CdcExtractor.Domain/Events/: BatchStarted.cs, BatchFinished.cs, DatasetCommitted.cs, DatasetFailed.cs, CdcGapDetected.cs, TableReBootstrapFlagged.cs, SchemaChanged.cs, PrerequisiteCheckFailed.cs
- [X] T023 [P] Implement config models as records per quickstart.md config.json structure in src/CdcExtractor.Contracts/Config/: AppConfig.cs, SqlServerConfig.cs, DownstreamConfig.cs, ScheduleConfig.cs, CdcConfig.cs, ExtractionConfig.cs
- [X] T024 [P] Define IExtractorService interface and DTO records per ipc-contract.md in src/CdcExtractor.Contracts/Ipc/: IExtractorService.cs (all JSON-RPC method signatures), ServiceStatusDto.cs, BatchProgressDto.cs, LogEntryDto.cs, DiagnosticCheckDto.cs
- [X] T025 Implement SqlConnectionFactory (create connections from SqlServerConfig, support Windows/AD auth and SQL Login, Encrypt=True) in src/CdcExtractor.Infrastructure/SqlServer/SqlConnectionFactory.cs
- [X] T026 Implement StateStoreInitializer with idempotent DDL for \_\_ExtractorTableStates, \_\_ExtractorBatchHistory, \_\_ExtractorDatasetHistory, \_\_ExtractorConfig tables per data-model.md schema in src/CdcExtractor.Infrastructure/StateStore/StateStoreInitializer.cs
- [X] T027 Implement DapperStateStore (IStateStore: Get/GetAll/Upsert/Delete table states with parameterized queries) in src/CdcExtractor.Infrastructure/StateStore/DapperStateStore.cs
- [X] T028 Implement DapperBatchHistoryStore (IBatchHistoryStore: Save/UpdateStatus/GetRecent/GetById batch runs with child datasets) in src/CdcExtractor.Infrastructure/StateStore/DapperBatchHistoryStore.cs
- [X] T029 [P] Configure Serilog (structured JSON format, file sink with daily rolling, Windows Event Log sink for Error+Critical, enrichers for CorrelationId/BatchId/TableName) in src/CdcExtractor.Service/Logging/SerilogSetup.cs

**Checkpoint**: Domain model, contracts, and state store infrastructure complete. All domain unit tests pass. User story implementation can begin.

---

## Phase 3: User Story 1 — Initial Setup & First Snapshot (Priority: P1) 🎯 MVP

**Goal**: Data engineer completes 9-step wizard to connect SQL Server, authorize downstream, select tables, enable CDC, configure schedule, and run the first full SNAPSHOT batch

**Independent Test**: Install the app on a clean Windows machine, complete the wizard against a test SQL Server database with 3–5 tables, verify SNAPSHOT batch succeeds and data appears in downstream

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T030 [P] [US1] Write unit tests for DiagnosticsService (mock ICdcManager + SqlConnectionFactory, verify all prerequisite checks: connectivity, SQL Agent, permissions, CDC database status) in tests/CdcExtractor.Application.Tests/DiagnosticsServiceTests.cs
- [ ] T031 [P] [US1] Write unit tests for SnapshotService (mock ICdcReader + IDownstreamClient, verify SNAPSHOT isolation LSN capture per R-001, chunk upload, dataset commit, LSN advancement only after commit) in tests/CdcExtractor.Application.Tests/SnapshotServiceTests.cs
- [ ] T032 [P] [US1] Write unit tests for ChunkingService (verify CSV generation, gzip compression, MaxBytes chunk splitting, header row presence) in tests/CdcExtractor.Application.Tests/ChunkingServiceTests.cs
- [ ] T033 [P] [US1] Write unit tests for SchemaService (mock ISchemaInspector, verify manifest generation with all column metadata, hash computation, hash comparison) in tests/CdcExtractor.Application.Tests/SchemaServiceTests.cs
- [ ] T034 [P] [US1] Write unit tests for ExtractionOrchestrator snapshot path (mock all dependencies, verify batch lifecycle: create batch → create datasets → upload chunks → commit datasets → finish batch SUCCEEDED/FAILED) in tests/CdcExtractor.Application.Tests/ExtractionOrchestratorTests.cs
- [ ] T035 [P] [US1] Write unit tests for CsvChunkWriter (verify RFC 4180 CSV format, gzip output, column mapping, service columns for snapshot) in tests/CdcExtractor.Infrastructure.Tests/Csv/CsvChunkWriterTests.cs
- [ ] T036 [P] [US1] Write unit tests for DownstreamClient (mock HttpClient via HttpMessageHandler, verify all API calls per downstream-api-client.md: create/heartbeat/finish batch, create dataset, upload chunk, commit/abort dataset, upload schema, error reporting) in tests/CdcExtractor.Infrastructure.Tests/Http/DownstreamClientTests.cs
- [ ] T037 [P] [US1] Write unit tests for DeviceFlowAuthenticator (mock HttpClient, verify device code request, polling loop with interval, authorization_pending handling, expired_token handling, successful token receipt) in tests/CdcExtractor.Infrastructure.Tests/Http/DeviceFlowAuthenticatorTests.cs

### Implementation for User Story 1

#### Infrastructure Layer

- [ ] T038 [P] [US1] Implement CdcManager (ICdcManager: IsCdcEnabledOnDatabase/EnableCdcOnDatabase, IsCdcEnabledOnTable/EnableCdcOnTable with capture instance naming, GetRetentionMinutes/SetRetentionMinutes, IsSqlAgentRunning) in src/CdcExtractor.Infrastructure/SqlServer/CdcManager.cs
- [ ] T039 [P] [US1] Implement SchemaInspector (ISchemaInspector: GetTableMetadata returning SchemaManifest with columns/PK/unique keys, GetAllTables returning TableDiscoveryInfo with CDC status, unique key presence, estimated row count, extraction mode) in src/CdcExtractor.Infrastructure/SqlServer/SchemaInspector.cs
- [ ] T040 [P] [US1] Implement CdcReader snapshot methods (ICdcReader: GetMaxLsn via sys.fn\_cdc\_get\_max\_lsn, GetMinLsn via sys.fn\_cdc\_get\_min\_lsn, ReadFullTable using SNAPSHOT isolation per R-001 algorithm) in src/CdcExtractor.Infrastructure/SqlServer/CdcReader.cs
- [ ] T041 [P] [US1] Implement CsvChunkWriter (CsvHelper + GZipStream, write header row, respect MaxBytes per chunk, flush on threshold) and CdcRowMapper (map DataRow/CdcChangeRow to CSV columns) in src/CdcExtractor.Infrastructure/Csv/CsvChunkWriter.cs and src/CdcExtractor.Infrastructure/Csv/CdcRowMapper.cs
- [ ] T042 [P] [US1] Implement DeviceFlowAuthenticator (POST /device/authorize for device code, poll POST /token with interval, handle authorization\_pending/expired\_token, return tokens on success) per downstream-api-client.md OAuth section in src/CdcExtractor.Infrastructure/Http/DeviceFlowAuthenticator.cs
- [ ] T043 [P] [US1] Implement DpapiTokenStore (encrypt refresh token via ProtectedData.Protect with CurrentUser scope, store as Base64 file, decrypt via ProtectedData.Unprotect) in src/CdcExtractor.Infrastructure/Http/DpapiTokenStore.cs
- [ ] T044 [US1] Implement DownstreamClient (IDownstreamClient: CreateBatch, Heartbeat, FinishBatch, ReportError, CreateDataset, UploadChunk, CommitDataset, AbortDataset, UploadSchema per downstream-api-client.md with Polly retry for 429/500/503) in src/CdcExtractor.Infrastructure/Http/DownstreamClient.cs

#### Application Layer

- [ ] T045 [US1] Implement DiagnosticsService (IDiagnosticsService: RunAllChecks — SQL connectivity, SQL Agent running, CDC database enabled, CDC table permissions, downstream HTTPS reachability, token validity) in src/CdcExtractor.Application/Services/DiagnosticsService.cs
- [ ] T046 [US1] Implement SchemaService (get SchemaManifest per table via ISchemaInspector, compute SchemaHash, upload schema to downstream via IDownstreamClient.UploadSchema) in src/CdcExtractor.Application/Services/SchemaService.cs
- [ ] T047 [US1] Implement ChunkingService (convert IAsyncEnumerable\<DataRow\> to gzip CSV chunks via CsvChunkWriter, respect MaxBytes, return list of ChunkResult) in src/CdcExtractor.Application/Services/ChunkingService.cs
- [ ] T048 [US1] Implement SnapshotService (per-table snapshot: begin SNAPSHOT TX, capture max LSN per R-001, read full table via ICdcReader, chunk via ChunkingService, upload chunks + commit dataset via IDownstreamClient) in src/CdcExtractor.Application/Services/SnapshotService.cs
- [ ] T049 [US1] Implement ExtractionOrchestrator snapshot path (create SNAPSHOT batch in downstream, iterate all tables, call SchemaService + SnapshotService per table, update TableState + BatchRun in state store, finish batch) in src/CdcExtractor.Application/Services/ExtractionOrchestrator.cs
- [ ] T050 [P] [US1] Implement ExtractionPlan and ChunkResult models in src/CdcExtractor.Application/Models/ExtractionPlan.cs and src/CdcExtractor.Application/Models/ChunkResult.cs
- [ ] T051 [P] [US1] Implement CdcSetupService (enable CDC on database, enable CDC per selected table with capture instance, set retention to configured minimum without lowering existing higher values) in src/CdcExtractor.Application/Services/CdcSetupService.cs

#### Service Layer (minimal for wizard Apply step)

- [ ] T052 [US1] Implement minimal Service host in src/CdcExtractor.Service/Program.cs: Generic Host builder, DI registration for all infrastructure + application services, IOptions\<AppConfig\> binding, console mode via --console flag, Windows Service support
- [ ] T053 [US1] Implement IpcServer (create Named Pipe "SQLExtractorIPC", StreamJsonRpc host, pipe ACL restricting access to authorized accounts, accept client connections) in src/CdcExtractor.Service/Ipc/IpcServer.cs
- [ ] T054 [US1] Implement ExtractorServiceRpc stub (IExtractorService: implement getStatus + getBatchProgress with real data, remaining methods return placeholder responses) in src/CdcExtractor.Service/Ipc/ExtractorServiceRpc.cs

#### WPF Application Layer

- [ ] T055 [US1] Create WPF App shell: App.xaml with merged resource dictionaries from Themes/, MainWindow.xaml with navigation frame for Wizard/Manager modes in src/CdcExtractor.App/
- [ ] T056 [P] [US1] Create theme resource dictionaries per FR-035 design system in src/CdcExtractor.App/Themes/: Colors.xaml, Typography.xaml, Buttons.xaml, Inputs.xaml, Tags.xaml, Cards.xaml, Shared.xaml
- [ ] T057 [P] [US1] Implement reusable controls in src/CdcExtractor.App/Controls/: WizardStepper.xaml (9-step indicator with current/complete/pending states), StatusChip.xaml (colored status badge), ProgressRow.xaml (table name + progress bar + percentage)
- [ ] T058 [US1] Implement NavigationService (page navigation within Wizard and Manager frames) and ConfigService (read/write config.json, validate) in src/CdcExtractor.App/Services/NavigationService.cs and src/CdcExtractor.App/Services/ConfigService.cs
- [ ] T059 [US1] Implement IpcClient (Named Pipe client via StreamJsonRpc, connect/disconnect/reconnect, call IExtractorService methods, handle connection errors) in src/CdcExtractor.App/Services/IpcClient.cs
- [ ] T060 [US1] Implement MainViewModel (detect first launch → Wizard mode vs existing config → Manager mode, navigation between modes) in src/CdcExtractor.App/ViewModels/MainViewModel.cs
- [ ] T061 [US1] Implement WizardViewModel (shared wizard state across 9 steps, step navigation with validation gating, Back/Next/Cancel commands, progress tracking) in src/CdcExtractor.App/ViewModels/Wizard/WizardViewModel.cs
- [ ] T062 [US1] Implement WelcomePage + WelcomeViewModel (Step 1: run environment checks — OS version, .NET runtime, service registration availability — display results with OK/FAIL) in src/CdcExtractor.App/Views/Wizard/WelcomePage.xaml and src/CdcExtractor.App/ViewModels/Wizard/WelcomeViewModel.cs
- [ ] T063 [US1] Implement ConnectSqlPage + ConnectSqlViewModel (Step 2: server/instance/database inputs, auth type radio buttons Windows/AD vs SQL Login, Test Connection command, prerequisites results display) in src/CdcExtractor.App/Views/Wizard/ConnectSqlPage.xaml and src/CdcExtractor.App/ViewModels/Wizard/ConnectSqlViewModel.cs
- [ ] T064 [US1] Implement DownstreamAuthPage + DownstreamAuthViewModel (Step 3: OAuth Device Flow UI — request device code, display user\_code + verification\_uri with copy button, poll status indicator, success/error/timeout states) in src/CdcExtractor.App/Views/Wizard/DownstreamAuthPage.xaml and src/CdcExtractor.App/ViewModels/Wizard/DownstreamAuthViewModel.cs
- [ ] T065 [US1] Implement SelectTablesPage + SelectTablesViewModel (Step 4: table list with search/filter, columns for schema/name/CDC status/unique key/rows/mode, mode badges CDC/SNAP/BLOCKED with tooltip reasons, checkbox selection, BLOCKED tables unselectable) in src/CdcExtractor.App/Views/Wizard/SelectTablesPage.xaml and src/CdcExtractor.App/ViewModels/Wizard/SelectTablesViewModel.cs
- [ ] T066 [US1] Implement CdcPolicyPage + CdcPolicyViewModel (Step 5: auto-enable CDC on database toggle, auto-enable CDC on tables toggle, retention minimum days input with default 7, batch inactivity TTL input with default 10 min, small-table snap threshold input with default 1000) in src/CdcExtractor.App/Views/Wizard/CdcPolicyPage.xaml and src/CdcExtractor.App/ViewModels/Wizard/CdcPolicyViewModel.cs
- [ ] T067 [US1] Implement SchedulePage + ScheduleViewModel (Step 6: list of run times with add/remove, 6-field cron expression preview per time, timezone selection dropdown, default times 08:00/12:00/18:00) in src/CdcExtractor.App/Views/Wizard/SchedulePage.xaml and src/CdcExtractor.App/ViewModels/Wizard/ScheduleViewModel.cs
- [ ] T068 [US1] Implement ReviewApplyPage + ReviewApplyViewModel (Step 7: configuration summary panel, Apply button with progress bar, step-by-step results checklist: service installed ✓, CDC enabled on DB ✓, CDC enabled per table ✓, retention set ✓, config saved ✓, state store initialized ✓) in src/CdcExtractor.App/Views/Wizard/ReviewApplyPage.xaml and src/CdcExtractor.App/ViewModels/Wizard/ReviewApplyViewModel.cs
- [ ] T069 [US1] Implement BootstrapRunPage + BootstrapRunViewModel (Step 8: optional initial SNAPSHOT trigger, per-table ProgressRow components, live log panel via IPC subscribeLogs, cancel button with graceful abort) in src/CdcExtractor.App/Views/Wizard/BootstrapRunPage.xaml and src/CdcExtractor.App/ViewModels/Wizard/BootstrapRunViewModel.cs
- [ ] T070 [US1] Implement DonePage + DoneViewModel (Step 9: setup summary — service status Running/Stopped, CDC enabled table count, snapshot result SUCCEEDED/FAILED/SKIPPED, next scheduled run time, "Open Manager" button) in src/CdcExtractor.App/Views/Wizard/DonePage.xaml and src/CdcExtractor.App/ViewModels/Wizard/DoneViewModel.cs

**Checkpoint**: Wizard fully functional end-to-end. First SNAPSHOT batch runs and uploads data to downstream. **MVP achievable at this point.**

---

## Phase 4: User Story 2 — Incremental Delta Extraction (Priority: P2)

**Goal**: Windows Service runs on schedule, extracts CDC changes as DELTA batches with \_op/\_lsn/\_seqval/\_ts columns, advances LSN only after successful downstream commit

**Independent Test**: After a successful SNAPSHOT, insert/update/delete rows in source database, trigger a scheduled run, verify DELTA batch contains exactly the expected changes with correct \_op values

### Tests for User Story 2 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T071 [P] [US2] Write unit tests for DeltaService (mock ICdcReader + IDownstreamClient, verify LSN range calculation using fn\_cdc\_increment\_lsn, CDC row reading with \_op/\_lsn/\_seqval/\_ts mapping, LSN advancement ONLY after dataset commit, no advancement on failure) in tests/CdcExtractor.Application.Tests/DeltaServiceTests.cs
- [ ] T072 [P] [US2] Write unit tests for SchedulerWorker (mock Cronos expressions, verify next run time calculation, single-instance SemaphoreSlim lock, parallel run prevention, graceful shutdown via CancellationToken) in tests/CdcExtractor.Service.Tests/SchedulerWorkerTests.cs
- [ ] T073 [P] [US2] Write unit tests for TokenRefreshHandler (mock HttpMessageHandler, verify Bearer token injection, 401 response triggers refresh with SemaphoreSlim guard, proactive refresh when \<5 min remaining, retry after refresh) in tests/CdcExtractor.Infrastructure.Tests/Http/TokenRefreshHandlerTests.cs

### Implementation for User Story 2

- [ ] T074 [US2] Extend CdcReader with ReadAllChangesAsync (read CDC changes via cdc.fn\_cdc\_get\_all\_changes in LSN range, map \_\_$operation to \_op I/U/D, include \_lsn/\_seqval/\_ts service columns) in src/CdcExtractor.Infrastructure/SqlServer/CdcReader.cs
- [ ] T075 [US2] Implement DeltaService (read last\_processed\_lsn from state store, compute to\_lsn via GetMaxLsn, increment from\_lsn via fn\_cdc\_increment\_lsn, read changes, chunk + upload, advance LSN only after successful dataset commit) in src/CdcExtractor.Application/Services/DeltaService.cs
- [ ] T076 [US2] Extend ExtractionOrchestrator with delta path (determine DELTA batch type, route CDC-mode tables to DeltaService, route SNAP-mode tables to SnapshotService, handle per-table errors as table-level failures without stopping batch, set batch FAILED if any table fails) in src/CdcExtractor.Application/Services/ExtractionOrchestrator.cs
- [ ] T077 [US2] Implement SchedulerWorker (BackgroundService: parse cron expressions via Cronos, calculate next run time, Task.Delay until next run, single-instance SemaphoreSlim(1,1) lock, invoke ExtractionOrchestrator, CancellationToken for graceful shutdown) in src/CdcExtractor.Service/Workers/SchedulerWorker.cs
- [ ] T078 [US2] Implement HeartbeatWorker (background loop: send heartbeat to downstream every 120 seconds while batch is active, handle 409 lease conflict by signaling batch abort, stop when batch finishes) in src/CdcExtractor.Service/Workers/HeartbeatWorker.cs
- [ ] T079 [US2] Implement TokenRefreshHandler (DelegatingHandler: inject Bearer token from ITokenProvider, on 401 acquire SemaphoreSlim + refresh via /token with grant\_type=refresh\_token + retry request, proactive refresh if expires\_at \< 5 min) in src/CdcExtractor.Infrastructure/Http/TokenRefreshHandler.cs
- [ ] T080 [US2] Register TokenRefreshHandler in IHttpClientFactory pipeline and wire SchedulerWorker + HeartbeatWorker as hosted services in DI in src/CdcExtractor.Service/Program.cs

**Checkpoint**: Scheduled DELTA extraction runs automatically. CDC changes captured correctly with service columns. Heartbeat prevents batch TTL expiration. Single-instance lock enforced.

---

## Phase 5: User Story 3 — Monitoring & Troubleshooting via Management Console (Priority: P3)

**Goal**: Operator opens Manager mode to monitor service health, view batch history, inspect per-table status, run diagnostics, and read live logs — all via IPC connection to the running Windows Service

**Independent Test**: Start the service with a known configuration, open the management console, verify all 7 screens display correct data, trigger a run and observe live progress

### Tests for User Story 3 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T081 [P] [US3] Write unit tests for ExtractorServiceRpc (mock all dependencies, verify all IPC methods return correct DTOs per ipc-contract.md: getStatus, getBatchProgress, getRecentBatches, getBatchDetails, getTableStates, runDiagnostics, subscribeLogs, getRecentLogs) in tests/CdcExtractor.Service.Tests/IpcServerTests.cs
- [ ] T082 [P] [US3] Write unit tests for DashboardViewModel (mock IpcClient, verify service status display, recent batches loading, live progress bar updates via onBatchProgress, log panel via onLogEntry) in tests/CdcExtractor.App.Tests/ViewModels/DashboardViewModelTests.cs
- [ ] T083 [P] [US3] Write unit tests for WizardViewModel (verify 9-step navigation, validation gating per step, Back/Next enable/disable logic, shared state propagation between steps) in tests/CdcExtractor.App.Tests/ViewModels/WizardViewModelTests.cs

### Implementation for User Story 3

#### Service Layer (IPC completion)

- [ ] T084 [US3] Complete ExtractorServiceRpc with all IPC methods per ipc-contract.md (getStatus, getBatchProgress, getRecentBatches, getBatchDetails, getTableStates, runDiagnostics, subscribeLogs, unsubscribeLogs, getRecentLogs) in src/CdcExtractor.Service/Ipc/ExtractorServiceRpc.cs
- [ ] T085 [US3] Implement LogBroadcaster (manage client log subscriptions with minimum level filter, thread-safe broadcast of LogEntryDto to all subscribers, remove subscriber on disconnect) in src/CdcExtractor.Service/Ipc/LogBroadcaster.cs
- [ ] T086 [US3] Implement IpcLogSink (custom Serilog ILogEventSink that forwards log events to LogBroadcaster, include CorrelationId/BatchId/Table properties in LogEntryDto) in src/CdcExtractor.Service/Logging/IpcLogSink.cs

#### WPF Manager Views

- [ ] T087 [P] [US3] Implement LogConsole control (scrollable log entries color-coded by severity: green INFO / yellow WARN / red ERROR, auto-scroll toggle, pause button, search textbox, copy and export commands) in src/CdcExtractor.App/Controls/LogConsole.xaml
- [ ] T088 [P] [US3] Implement TagBadge control (colored status tags: SUCCEEDED green, FAILED red, RUNNING blue, ABORTED gray, SNAPSHOT/DELTA type badges) in src/CdcExtractor.App/Controls/TagBadge.xaml
- [ ] T089 [US3] Implement DashboardPage + DashboardViewModel (service status card, last run summary with duration, CDC lag indicator, current activity with per-table ProgressRow components updating via onBatchProgress, recent runs list, live logs panel via subscribeLogs) in src/CdcExtractor.App/Views/Manager/DashboardPage.xaml and src/CdcExtractor.App/ViewModels/Manager/DashboardViewModel.cs
- [ ] T090 [US3] Implement RunsPage + RunsViewModel (filterable/sortable batch list with columns: type TagBadge, status TagBadge, trigger, started/finished times, duration, table count, row count; row click navigates to RunDetails) in src/CdcExtractor.App/Views/Manager/RunsPage.xaml and src/CdcExtractor.App/ViewModels/Manager/RunsViewModel.cs
- [ ] T091 [US3] Implement RunDetailsPage + RunDetailsViewModel (batch header with status, per-table dataset list: table name, LSN range, rows, chunks, status, errors with code + message, filtered logs for that batch via getRecentLogs with batchId filter) in src/CdcExtractor.App/Views/Manager/RunDetailsPage.xaml and src/CdcExtractor.App/ViewModels/Manager/RunDetailsViewModel.cs
- [ ] T092 [US3] Implement TablesPage + TablesViewModel (all tracked tables: mode CDC/SNAP, CDC enabled status, last processed LSN, last sync time, lag in minutes, bootstrap status, error message, re-init action button) in src/CdcExtractor.App/Views/Manager/TablesPage.xaml and src/CdcExtractor.App/ViewModels/Manager/TablesViewModel.cs
- [ ] T093 [US3] Implement DiagnosticsPage + DiagnosticsViewModel (run all checks via runDiagnostics IPC, display grouped by category SqlServer/Permissions/Downstream/Ipc, each check with OK/WARN/FAIL StatusChip + detail + remediation hint) in src/CdcExtractor.App/Views/Manager/DiagnosticsPage.xaml and src/CdcExtractor.App/ViewModels/Manager/DiagnosticsViewModel.cs
- [ ] T094 [US3] Implement SettingsPage + SettingsViewModel (view/edit: schedule cron expressions with add/remove, CDC retention + batch TTL + snap threshold, SQL Server connection details, downstream API base URL + client ID, config file path, save + reload commands) in src/CdcExtractor.App/Views/Manager/SettingsPage.xaml and src/CdcExtractor.App/ViewModels/Manager/SettingsViewModel.cs
- [ ] T095 [US3] Implement LogsPage + LogsViewModel (full log stream via IPC subscribeLogs, severity filter dropdown INFO/WARN/ERROR, search textbox, auto-scroll toggle, pause/resume, copy selection, export to file) in src/CdcExtractor.App/Views/Manager/LogsPage.xaml and src/CdcExtractor.App/ViewModels/Manager/LogsViewModel.cs

**Checkpoint**: All 7 Manager screens functional. Live log streaming works. Full observability into service operations. IPC contract fully implemented.

---

## Phase 6: User Story 4 — CDC Gap Detection & Re-bootstrap (Priority: P4)

**Goal**: Service detects when CDC history was cleaned before extraction (LSN gap), flags affected tables for re-bootstrap, reports error to downstream, and automatically re-bootstraps on the next snapshot run

**Independent Test**: Manually reduce CDC retention to force cleanup of change tables, trigger a run, verify the system detects the gap, does NOT extract partial data, flags the table RE\_BOOTSTRAP, and the next snapshot re-establishes the LSN baseline

### Tests for User Story 4 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T096 [P] [US4] Write unit tests for CDC gap detection (mock ICdcReader.GetMinLsn returning LSN > last\_processed\_lsn, verify: CdcGapDetected event raised, table status set to RE\_BOOTSTRAP, no partial data extracted, error reported to downstream with code CDC\_GAP\_DETECTED and is\_terminal true) in tests/CdcExtractor.Application.Tests/DeltaServiceTests.cs (extend)
- [ ] T097 [P] [US4] Write unit tests for re-bootstrap flow (mock table with BootstrapStatus.ReBootstrap, verify ExtractionOrchestrator routes to SnapshotService for full re-extract, new LSN baseline stored, status reset to Complete) in tests/CdcExtractor.Application.Tests/ExtractionOrchestratorTests.cs (extend)

### Implementation for User Story 4

- [ ] T098 [US4] Add CDC gap detection to DeltaService: before reading changes, compare last\_processed\_lsn with ICdcReader.GetMinLsn, if gap detected raise CdcGapDetected event + set table BootstrapStatus to ReBootstrap + skip table (no partial extraction) in src/CdcExtractor.Application/Services/DeltaService.cs
- [ ] T099 [US4] Add re-bootstrap logic to ExtractionOrchestrator: detect tables with RE\_BOOTSTRAP status, route to SnapshotService for full re-extract, report CDC\_GAP\_DETECTED error to downstream per downstream-api-client.md error format, reset status to Complete after successful snapshot in src/CdcExtractor.Application/Services/ExtractionOrchestrator.cs
- [ ] T100 [US4] Update TablesPage to display RE\_BOOTSTRAP status with StatusChip, explanation message ("CDC gap detected — history was cleaned before extraction. Table will be re-bootstrapped on next snapshot run."), and recommended actions in src/CdcExtractor.App/Views/Manager/TablesPage.xaml and src/CdcExtractor.App/ViewModels/Manager/TablesViewModel.cs

**Checkpoint**: CDC gap detection is 100% reliable. No silent data loss. Re-bootstrap recovers automatically on next run.

---

## Phase 7: User Story 5 — Manual Run Trigger (Priority: P5)

**Goal**: Operator triggers a batch run immediately from the Management Console Dashboard without waiting for the next scheduled time

**Independent Test**: Open Manager, click "Run Now", verify a batch starts immediately with trigger type MANUAL, runs to completion, and appears in the Runs list

### Tests for User Story 5 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T101 [P] [US5] Write unit tests for manual run trigger (mock SchedulerWorker lock state, verify: triggerRun accepted when no batch running + returns batchId, triggerRun rejected when batch in progress + returns reason, triggered batch has trigger MANUAL) in tests/CdcExtractor.Service.Tests/SchedulerWorkerTests.cs (extend)

### Implementation for User Story 5

- [ ] T102 [US5] Implement triggerRun in ExtractorServiceRpc (check single-instance SemaphoreSlim, start batch via ExtractionOrchestrator with BatchTrigger.Manual, return accepted/rejected response per ipc-contract.md) and add TriggerManualRunAsync to SchedulerWorker in src/CdcExtractor.Service/Ipc/ExtractorServiceRpc.cs and src/CdcExtractor.Service/Workers/SchedulerWorker.cs
- [ ] T103 [US5] Add "Run Now" button to DashboardPage (disabled when batch is active per getBatchProgress, enabled when idle, click calls IpcClient.triggerRun, show success toast or "already running" message) in src/CdcExtractor.App/Views/Manager/DashboardPage.xaml and src/CdcExtractor.App/ViewModels/Manager/DashboardViewModel.cs

**Checkpoint**: Manual runs work alongside scheduled runs. Single-instance lock prevents conflicts. Manual batch shows trigger MANUAL in Runs list.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Error handling hardening, security, performance, and final validation across all user stories

- [ ] T104 [P] Implement global exception handling in Service host (unhandled exception logging to Serilog, graceful shutdown on fatal errors, write Critical events to Windows Event Log) in src/CdcExtractor.Service/Program.cs
- [ ] T105 [P] Add Polly retry policies for SQL Server transient faults (deadlocks SqlException 1205, timeouts SqlException -2, connection drops) to SqlConnectionFactory as shared retry policy for all Dapper calls in src/CdcExtractor.Infrastructure/SqlServer/SqlConnectionFactory.cs
- [ ] T106 [P] Implement error classification in ExtractionOrchestrator: table-level errors (permission denied, CDC disabled) skip table and continue batch, batch-level errors (downstream unreachable, lease conflict) stop entire batch, set batch status FAILED vs ABORTED accordingly in src/CdcExtractor.Application/Services/ExtractionOrchestrator.cs
- [ ] T107 [P] Add Named Pipe ACL security (restrict pipe access to service account SID + configured Windows user/group SID, deny all others) to IpcServer in src/CdcExtractor.Service/Ipc/IpcServer.cs
- [ ] T108 Validate all error messages are actionable per FR-036 and FR-041: every error includes table name, batch ID, LSN range (where applicable), human-readable description, and remediation hint — audit across all services and UI
- [ ] T109 Run quickstart.md validation: dotnet build src/CdcExtractor.sln succeeds, dotnet test tests/ all pass, service starts in console mode (--console), WPF app launches to wizard on first run

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Phase 2 — delivers MVP
- **US2 (Phase 4)**: Depends on Phase 2; reuses US1 infrastructure (DownstreamClient, CsvChunkWriter, ExtractionOrchestrator)
- **US3 (Phase 5)**: Depends on Phase 2; benefits from US1 IpcServer stub and US2 SchedulerWorker
- **US4 (Phase 6)**: Depends on US2 (DeltaService must exist for gap detection) ❌ sequential after US2
- **US5 (Phase 7)**: Depends on US2 (SchedulerWorker) + US3 (Dashboard for UI) ❌ sequential after US2 + US3
- **Polish (Phase 8)**: Depends on all user stories complete

### User Story Dependencies

- **US1 (P1)**: Foundation only — fully independent ✅
- **US2 (P2)**: Foundation + reuses US1 infrastructure components (DownstreamClient, CsvChunkWriter, CdcReader, ExtractionOrchestrator) ⚠️
- **US3 (P3)**: Foundation + minimal dependency on US1/US2 service layer ⚠️
- **US4 (P4)**: Requires US2 DeltaService ❌ sequential after US2
- **US5 (P5)**: Requires US2 SchedulerWorker + US3 Dashboard ❌ sequential after US2 + US3

### Within Each User Story

- Tests MUST be written and FAIL before implementation (TDD — constitution Principle I)
- Infrastructure layer before Application layer
- Application services before Service/UI layer
- Core logic before integration wiring

### Parallel Opportunities

- **Phase 1**: T003 ∥ T004 (after T001+T002)
- **Phase 2**: T005–T009 all in parallel; T010–T024 mostly in parallel; T025→T026→T027→T028 sequential (state store depends on connection factory and DDL)
- **Phase 3**: T030–T037 all in parallel; T038–T043 all in parallel; T056 ∥ T057; wizard pages T062–T070 are independent files but share WizardViewModel (T061)
- **Phase 4**: T071 ∥ T072 ∥ T073
- **Phase 5**: T081 ∥ T082 ∥ T083; T087 ∥ T088
- **Phase 8**: T104 ∥ T105 ∥ T106 ∥ T107
- **Cross-phase**: US1 and US2 CAN overlap if US1 shared infrastructure (T038–T044) completes first

---

## Parallel Example: User Story 1

```
# Launch all US1 tests in parallel (T030–T037):
T030: DiagnosticsService tests
T031: SnapshotService tests
T032: ChunkingService tests
T033: SchemaService tests
T034: ExtractionOrchestrator tests
T035: CsvChunkWriter tests
T036: DownstreamClient tests
T037: DeviceFlowAuthenticator tests

# Launch all US1 infrastructure in parallel (T038–T043):
T038: CdcManager
T039: SchemaInspector
T040: CdcReader (snapshot methods)
T041: CsvChunkWriter + CdcRowMapper
T042: DeviceFlowAuthenticator
T043: DpapiTokenStore

# Launch theme + controls in parallel:
T056: Theme resource dictionaries
T057: Wizard controls (WizardStepper, StatusChip, ProgressRow)
```

## Parallel Example: User Story 3

```
# Launch all US3 tests in parallel (T081–T083):
T081: ExtractorServiceRpc tests
T082: DashboardViewModel tests
T083: WizardViewModel tests

# Launch controls in parallel:
T087: LogConsole control
T088: TagBadge control
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T004)
2. Complete Phase 2: Foundational (T005–T029) — **CRITICAL, blocks all stories**
3. Complete Phase 3: User Story 1 (T030–T070)
4. **STOP and VALIDATE**: Test wizard end-to-end against real SQL Server
5. Deploy/demo if ready — first SNAPSHOT works! 🎯

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 → Test independently → **MVP! First snapshot works** 🎯
3. Add US2 → Test independently → Automated delta extraction on schedule
4. Add US3 → Test independently → Full monitoring and troubleshooting console
5. Add US4 → Test independently → CDC gap safety net (no silent data loss)
6. Add US5 → Test independently → On-demand manual runs
7. Polish → Production-ready

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US1 (wizard + snapshot) — T030–T070
   - Developer B: US2 (delta + scheduler) — T071–T080 (starts after US1 infra T038–T044)
3. After US1 + US2:
   - Developer A: US3 (manager console) — T081–T095
   - Developer B: US4 (gap detection) + US5 (manual trigger) — T096–T103
4. Everyone: Polish (Phase 8)

---

## Summary

| Phase | Scope | Tasks | Parallel Tasks |
|-------|-------|-------|----------------|
| Phase 1 | Setup | 4 (T001–T004) | T003 ∥ T004 |
| Phase 2 | Foundational | 25 (T005–T029) | 20 parallelizable |
| Phase 3 | US1 — Initial Setup & Snapshot | 41 (T030–T070) | 22 parallelizable |
| Phase 4 | US2 — Delta Extraction | 10 (T071–T080) | 3 test tasks parallel |
| Phase 5 | US3 — Management Console | 15 (T081–T095) | 5 parallelizable |
| Phase 6 | US4 — CDC Gap Detection | 5 (T096–T100) | 2 test tasks parallel |
| Phase 7 | US5 — Manual Run Trigger | 3 (T101–T103) | 1 test task parallel |
| Phase 8 | Polish | 6 (T104–T109) | 4 parallelizable |
| **Total** | | **109 tasks** | |

### Per User Story

| Story | Tasks | Test Tasks | Implementation Tasks |
|-------|-------|------------|---------------------|
| US1 (P1) | 41 | 8 (T030–T037) | 33 (T038–T070) |
| US2 (P2) | 10 | 3 (T071–T073) | 7 (T074–T080) |
| US3 (P3) | 15 | 3 (T081–T083) | 12 (T084–T095) |
| US4 (P4) | 5 | 2 (T096–T097) | 3 (T098–T100) |
| US5 (P5) | 3 | 1 (T101) | 2 (T102–T103) |

### MVP Scope

**Suggested MVP**: Phase 1 + Phase 2 + Phase 3 (US1) = **70 tasks**
This delivers: wizard setup, CDC configuration, first SNAPSHOT, downstream upload.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps each task to its specific user story for traceability
- Each user story is independently testable at its checkpoint
- TDD is NON-NEGOTIABLE — all test tasks written before implementation (constitution Principle I)
- Commit after each task or logical group
- Stop at any checkpoint to validate the story independently
- All error messages must be actionable (FR-036, FR-041, constitution Principle III)
- Format validated: ALL 109 tasks follow `- [ ] [TaskID] [P?] [Story?] Description with file path`
