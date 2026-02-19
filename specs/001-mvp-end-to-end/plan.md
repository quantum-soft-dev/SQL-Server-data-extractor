# Implementation Plan: MVP End-to-End CDC Data Extractor

**Branch**: `001-mvp-end-to-end` | **Date**: 2026-02-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-mvp-end-to-end/spec.md`

## Summary

Build the complete MVP end-to-end CDC data extraction system:
a .NET 8 Windows Service that extracts data from SQL Server via CDC
(snapshot + incremental deltas), uploads to a downstream HTTP API
as gzip CSV chunks, and is managed through a WPF desktop application
(configurator wizard + management console) connected via Named Pipes
IPC (JSON-RPC/StreamJsonRpc).

Key technical decisions from research:
- Snapshot consistency via SNAPSHOT isolation + LSN boundary capture
- State store in SQL Server via Dapper (not EF Core)
- OAuth Device Flow via raw HttpClient (not MSAL, downstream is not Azure AD)
- Scheduler via Cronos + custom BackgroundService
- CSV via CsvHelper, compression via GZipStream

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS)
**Primary Dependencies**: StreamJsonRpc, Dapper, Serilog, Polly,
CsvHelper, Cronos, CommunityToolkit.Mvvm, Microsoft.Data.SqlClient
**Storage**: SQL Server (same instance as source data; state tables
prefixed with `__Extractor`)
**Testing**: xUnit + NSubstitute + FluentAssertions; Testcontainers
for SQL Server integration tests
**Target Platform**: Windows 10/11, Windows Server 2016+
**Project Type**: Desktop (WPF) + Windows Service (multi-project solution)
**Performance Goals**: 1M-row snapshot within 4-hour schedule window
**Constraints**: Single-instance execution, at-least-once delivery,
no data loss on crash/restart
**Scale/Scope**: Single SQL Server database, ~50 tables, 9 wizard
screens + 7 manager screens

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. TDD (NON-NEGOTIABLE) | PASS | Test projects mirror source. Unit tests isolated via interfaces. Integration tests via Testcontainers. |
| II. DDD | PASS | Bounded contexts defined (Extraction, Scheduling, Sink, Configuration, Ipc, CdcManagement). Value Objects: Lsn, TableIdentifier, BatchId, SchemaHash. Repository pattern for state store. |
| III. Exception Handling (NON-NEGOTIABLE) | PASS | Domain exceptions defined (CdcGapException, SinkUploadException, etc.). Global handlers planned. CancellationToken throughout. |
| IV. Observability (NON-NEGOTIABLE) | PASS | Structured logging (Serilog), correlation IDs, Windows Event Log. Message format pattern defined. |
| V. .NET Best Practices | PASS | .NET 8, DI throughout, async/await, nullable refs, IOptions, records for DTOs. |
| VI. Reliability | PASS | LSN advanced only after commit. Idempotent keys. CDC gap detection. Polly retries. Graceful shutdown. |
| VII. Security | PASS | DPAPI for tokens. TLS for SQL. HTTPS for downstream. Named Pipe ACL. Parameterized queries. Bracketed identifiers. |
| VIII. Simplicity (YAGNI) | PASS | No features beyond Phase 1 scope. Dapper over EF Core. Cronos over Quartz. No premature abstractions. |

**Post-design re-check**: All principles PASS. No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/001-mvp-end-to-end/
├── plan.md                            # This file
├── spec.md                            # Feature specification
├── research.md                        # Phase 0 research decisions
├── data-model.md                      # Domain model & state store schema
├── quickstart.md                      # Build & run guide
├── contracts/
│   ├── ipc-contract.md                # Named Pipes JSON-RPC methods
│   └── downstream-api-client.md       # HTTP API client contract
└── tasks.md                           # (Phase 2 — /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── CdcExtractor.Domain/
│   ├── ValueObjects/
│   │   ├── Lsn.cs
│   │   ├── TableIdentifier.cs
│   │   ├── SchemaHash.cs
│   │   ├── BatchId.cs
│   │   └── DatasetId.cs
│   ├── Entities/
│   │   ├── TableState.cs
│   │   ├── BatchRun.cs
│   │   ├── DatasetRun.cs
│   │   └── SchemaManifest.cs
│   ├── Enums/
│   │   ├── ExtractionMode.cs
│   │   ├── BootstrapStatus.cs
│   │   ├── BatchType.cs
│   │   ├── BatchTrigger.cs
│   │   ├── BatchStatus.cs
│   │   └── DatasetStatus.cs
│   ├── Events/
│   │   ├── BatchStarted.cs
│   │   ├── BatchFinished.cs
│   │   ├── DatasetCommitted.cs
│   │   ├── CdcGapDetected.cs
│   │   └── ...
│   ├── Exceptions/
│   │   ├── CdcGapException.cs
│   │   ├── SinkUploadException.cs
│   │   ├── PrerequisiteCheckFailedException.cs
│   │   └── ...
│   └── Interfaces/
│       ├── IStateStore.cs
│       ├── IBatchHistoryStore.cs
│       ├── IDownstreamClient.cs
│       ├── ICdcReader.cs
│       ├── ICdcManager.cs
│       ├── ISchemaInspector.cs
│       ├── IDiagnosticsService.cs
│       ├── ITokenProvider.cs
│       └── IScheduler.cs
│
├── CdcExtractor.Application/
│   ├── Services/
│   │   ├── ExtractionOrchestrator.cs      # Main batch orchestration
│   │   ├── SnapshotService.cs             # Full table snapshot logic
│   │   ├── DeltaService.cs                # CDC incremental logic
│   │   ├── CdcSetupService.cs             # CDC enable/verify/retention
│   │   ├── DiagnosticsService.cs          # Prerequisites checks
│   │   ├── SchemaService.cs               # Schema detection & manifest
│   │   └── ChunkingService.cs             # CSV generation & chunking
│   └── Models/
│       ├── ExtractionPlan.cs              # Per-batch table plan
│       └── ChunkResult.cs
│
├── CdcExtractor.Infrastructure/
│   ├── SqlServer/
│   │   ├── CdcReader.cs                   # ICdcReader implementation
│   │   ├── CdcManager.cs                  # ICdcManager implementation
│   │   ├── SchemaInspector.cs             # ISchemaInspector implementation
│   │   └── SqlConnectionFactory.cs
│   ├── StateStore/
│   │   ├── DapperStateStore.cs            # IStateStore (SQL Server + Dapper)
│   │   ├── DapperBatchHistoryStore.cs     # IBatchHistoryStore
│   │   └── StateStoreInitializer.cs       # Idempotent table creation
│   ├── Http/
│   │   ├── DownstreamClient.cs            # IDownstreamClient
│   │   ├── TokenRefreshHandler.cs         # DelegatingHandler for OAuth
│   │   ├── DeviceFlowAuthenticator.cs     # OAuth Device Flow
│   │   └── DpapiTokenStore.cs             # DPAPI encrypted storage
│   └── Csv/
│       ├── CsvChunkWriter.cs              # CsvHelper + GZipStream
│       └── CdcRowMapper.cs                # Map CDC rows to CSV
│
├── CdcExtractor.Contracts/
│   ├── Ipc/
│   │   ├── IExtractorService.cs           # JSON-RPC method interface
│   │   ├── ServiceStatusDto.cs
│   │   ├── BatchProgressDto.cs
│   │   ├── LogEntryDto.cs
│   │   └── DiagnosticCheckDto.cs
│   └── Config/
│       ├── AppConfig.cs                   # Root config model
│       ├── SqlServerConfig.cs
│       ├── DownstreamConfig.cs
│       ├── ScheduleConfig.cs
│       ├── CdcConfig.cs
│       └── ExtractionConfig.cs
│
├── CdcExtractor.Service/
│   ├── Program.cs                         # Host builder + DI
│   ├── Workers/
│   │   ├── SchedulerWorker.cs             # BackgroundService + Cronos
│   │   └── HeartbeatWorker.cs             # Batch heartbeat loop
│   ├── Ipc/
│   │   ├── IpcServer.cs                   # Named Pipe + StreamJsonRpc host
│   │   ├── ExtractorServiceRpc.cs         # RPC method implementations
│   │   └── LogBroadcaster.cs              # Log subscription manager
│   └── Logging/
│       ├── SerilogSetup.cs
│       └── IpcLogSink.cs                  # Custom Serilog sink -> IPC
│
└── CdcExtractor.App/
    ├── App.xaml                            # Merged resource dictionaries
    ├── Themes/
    │   ├── Colors.xaml
    │   ├── Typography.xaml
    │   ├── Buttons.xaml
    │   ├── Inputs.xaml
    │   ├── Tags.xaml
    │   ├── Cards.xaml
    │   └── Shared.xaml
    ├── Controls/
    │   ├── StatusChip.xaml
    │   ├── TagBadge.xaml
    │   ├── LogConsole.xaml
    │   ├── WizardStepper.xaml
    │   └── ProgressRow.xaml
    ├── Views/
    │   ├── MainWindow.xaml
    │   ├── Wizard/
    │   │   ├── WelcomePage.xaml            # Step 1
    │   │   ├── ConnectSqlPage.xaml         # Step 2
    │   │   ├── DownstreamAuthPage.xaml     # Step 3
    │   │   ├── SelectTablesPage.xaml       # Step 4
    │   │   ├── CdcPolicyPage.xaml          # Step 5
    │   │   ├── SchedulePage.xaml           # Step 6
    │   │   ├── ReviewApplyPage.xaml        # Step 7
    │   │   ├── BootstrapRunPage.xaml       # Step 8
    │   │   └── DonePage.xaml               # Step 9
    │   └── Manager/
    │       ├── DashboardPage.xaml
    │       ├── RunsPage.xaml
    │       ├── RunDetailsPage.xaml
    │       ├── TablesPage.xaml
    │       ├── DiagnosticsPage.xaml
    │       ├── SettingsPage.xaml
    │       └── LogsPage.xaml
    ├── ViewModels/
    │   ├── MainViewModel.cs
    │   ├── Wizard/
    │   │   ├── WizardViewModel.cs          # Navigation + shared state
    │   │   ├── WelcomeViewModel.cs
    │   │   ├── ConnectSqlViewModel.cs
    │   │   ├── DownstreamAuthViewModel.cs
    │   │   ├── SelectTablesViewModel.cs
    │   │   ├── CdcPolicyViewModel.cs
    │   │   ├── ScheduleViewModel.cs
    │   │   ├── ReviewApplyViewModel.cs
    │   │   ├── BootstrapRunViewModel.cs
    │   │   └── DoneViewModel.cs
    │   └── Manager/
    │       ├── DashboardViewModel.cs
    │       ├── RunsViewModel.cs
    │       ├── RunDetailsViewModel.cs
    │       ├── TablesViewModel.cs
    │       ├── DiagnosticsViewModel.cs
    │       ├── SettingsViewModel.cs
    │       └── LogsViewModel.cs
    └── Services/
        ├── IpcClient.cs                    # Named Pipe client
        ├── NavigationService.cs
        └── ConfigService.cs

tests/
├── CdcExtractor.Domain.Tests/
│   ├── ValueObjects/
│   │   ├── LsnTests.cs
│   │   ├── TableIdentifierTests.cs
│   │   └── SchemaHashTests.cs
│   └── Entities/
│       ├── TableStateTests.cs
│       └── BatchRunTests.cs
├── CdcExtractor.Application.Tests/
│   ├── ExtractionOrchestratorTests.cs
│   ├── SnapshotServiceTests.cs
│   ├── DeltaServiceTests.cs
│   └── ChunkingServiceTests.cs
├── CdcExtractor.Infrastructure.Tests/
│   ├── SqlServer/
│   │   ├── CdcReaderIntegrationTests.cs
│   │   └── CdcManagerIntegrationTests.cs
│   ├── StateStore/
│   │   ├── DapperStateStoreIntegrationTests.cs
│   │   └── StateStoreInitializerTests.cs
│   ├── Http/
│   │   ├── DownstreamClientTests.cs
│   │   └── TokenRefreshHandlerTests.cs
│   └── Csv/
│       └── CsvChunkWriterTests.cs
├── CdcExtractor.Service.Tests/
│   ├── SchedulerWorkerTests.cs
│   └── IpcServerTests.cs
└── CdcExtractor.App.Tests/
    └── ViewModels/
        ├── WizardViewModelTests.cs
        └── DashboardViewModelTests.cs
```

**Structure Decision**: Multi-project .NET solution following Clean
Architecture / DDD layering. Domain at the center (no dependencies),
Application orchestrates use cases, Infrastructure implements ports,
Service and App are the entry points. This aligns with constitution
Principle II (DDD) and Principle V (.NET Best Practices — project
structure).

## Complexity Tracking

| Item | Why Needed | Simpler Alternative Rejected Because |
|------|------------|-------------------------------------|
| 6 source projects | Each has a distinct responsibility per constitution V | Fewer projects would mix domain logic with infrastructure or UI code, violating DDD boundaries |
| Repository pattern | Required by constitution II (DDD) for testability | Direct Dapper calls in application services would make unit testing impossible without a real database |
| DelegatingHandler for OAuth | Clean separation of auth from business logic | Manually adding Bearer tokens in each HTTP call duplicates code and misses refresh edge cases |
