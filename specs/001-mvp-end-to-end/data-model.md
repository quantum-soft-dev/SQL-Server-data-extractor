# Data Model: MVP End-to-End CDC Data Extractor

**Branch**: `001-mvp-end-to-end` | **Date**: 2026-02-15

## Domain Value Objects

### Lsn

Immutable wrapper around SQL Server LSN (`binary(10)`).

- `Value` : `byte[10]` — raw LSN bytes
- Comparable (implements `IComparable<Lsn>`)
- Formatting: hex string for display/serialization (`0x00000027000001E80003`)
- Factory: `Lsn.Parse(string hex)`, `Lsn.From(byte[] raw)`
- Special: `Lsn.Empty` (all zeros, represents "no LSN")

### TableIdentifier

Immutable identifier for a SQL Server table.

- `Schema` : `string` — e.g., `"dbo"`
- `Name` : `string` — e.g., `"Orders"`
- `FullName` : `string` — `"dbo.Orders"` (computed)
- `QuotedFullName` : `string` — `"[dbo].[Orders]"` (SQL-safe, computed)

### SchemaHash

Immutable hash of a table's schema manifest.

- `Value` : `string` — SHA-256 hex digest of the canonical JSON schema
- Factory: `SchemaHash.Compute(SchemaManifest manifest)`

### BatchId

Immutable identifier for a batch.

- `Value` : `Guid`
- Factory: `BatchId.New()`, `BatchId.Parse(string)`

### DatasetId

Immutable identifier for a dataset.

- `Value` : `Guid`
- Factory: `DatasetId.New()`, `DatasetId.Parse(string)`

## Domain Entities

### TableState

Per-table persistent state. Aggregate root for table tracking.

| Field | Type | Description |
|-------|------|-------------|
| `TableId` | `TableIdentifier` | Primary key (schema.table) |
| `ExtractionMode` | `ExtractionMode` | CDC or SNAP |
| `LastProcessedLsn` | `Lsn` | Last successfully committed LSN |
| `BootstrapStatus` | `BootstrapStatus` | PENDING / COMPLETE / RE_BOOTSTRAP |
| `SchemaHash` | `SchemaHash` | Current schema version hash |
| `LastSyncTime` | `DateTimeOffset?` | Timestamp of last successful commit |
| `CaptureInstance` | `string?` | CDC capture instance name |
| `ErrorMessage` | `string?` | Last error (null if OK) |

**State transitions**:
```
PENDING --[snapshot committed]--> COMPLETE
COMPLETE --[CDC gap detected]--> RE_BOOTSTRAP
RE_BOOTSTRAP --[snapshot committed]--> COMPLETE
```

**Enums**:
```
ExtractionMode { Cdc, Snap }
BootstrapStatus { Pending, Complete, ReBootstrap }
```

### BatchRun

Represents a single extraction run. Aggregate root.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `BatchId` | Unique identifier |
| `RemoteBatchId` | `string?` | Downstream batch ID |
| `Type` | `BatchType` | SNAPSHOT or DELTA |
| `Trigger` | `BatchTrigger` | SCHEDULED or MANUAL |
| `Status` | `BatchStatus` | RUNNING / SUCCEEDED / FAILED / ABORTED |
| `LeaseToken` | `string?` | Fence token from downstream |
| `StartedAt` | `DateTimeOffset` | Batch start timestamp |
| `FinishedAt` | `DateTimeOffset?` | Batch end timestamp |
| `Datasets` | `IReadOnlyList<DatasetRun>` | Child datasets |

**Enums**:
```
BatchType { Snapshot, Delta }
BatchTrigger { Scheduled, Manual }
BatchStatus { Running, Succeeded, Failed, Aborted }
```

### DatasetRun

Extraction of one table within a batch. Owned by BatchRun.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `DatasetId` | Unique identifier |
| `RemoteDatasetId` | `string?` | Downstream dataset ID |
| `TableId` | `TableIdentifier` | Which table |
| `FromLsn` | `Lsn?` | Start LSN (null for snapshot) |
| `ToLsn` | `Lsn?` | End LSN (null for snapshot) |
| `Status` | `DatasetStatus` | CREATED / UPLOADING / COMMITTED / ABORTED / SKIPPED |
| `RowCount` | `long` | Total rows extracted |
| `ChunkCount` | `int` | Number of chunks uploaded |
| `BytesSent` | `long` | Total bytes sent (compressed) |
| `ErrorCode` | `string?` | Error code if failed |
| `ErrorMessage` | `string?` | Human-readable error |

**Enums**:
```
DatasetStatus { Created, Uploading, Committed, Aborted, Skipped }
```

### SchemaManifest

JSON description of a table's structure.

| Field | Type | Description |
|-------|------|-------------|
| `Table` | `TableIdentifier` | Table reference |
| `CapturedAt` | `DateTimeOffset` | When captured |
| `Hash` | `SchemaHash` | Computed hash |
| `Columns` | `IReadOnlyList<ColumnInfo>` | Column definitions |
| `PrimaryKey` | `IReadOnlyList<string>` | PK column names |
| `UniqueKeys` | `IReadOnlyList<IReadOnlyList<string>>` | Unique key sets |
| `WatermarkColumn` | `string?` | Optional watermark column |

### ColumnInfo

| Field | Type | Description |
|-------|------|-------------|
| `Name` | `string` | Column name |
| `SqlType` | `string` | SQL Server type (e.g., `nvarchar`) |
| `IsNullable` | `bool` | Nullable flag |
| `MaxLength` | `int?` | Max length for string/binary |
| `Precision` | `byte?` | Precision for decimal/numeric |
| `Scale` | `byte?` | Scale for decimal/numeric |
| `OrdinalPosition` | `int` | Column order |

### ScheduleConfig

| Field | Type | Description |
|-------|------|-------------|
| `CronExpressions` | `IReadOnlyList<string>` | 6-field cron expressions |
| `Timezone` | `string` | IANA timezone ID |
| `PreventParallelRuns` | `bool` | Single-instance lock (always true in MVP) |

### DiagnosticCheck

| Field | Type | Description |
|-------|------|-------------|
| `Name` | `string` | Check name (e.g., "SQL Agent") |
| `Category` | `DiagnosticCategory` | SqlServer / Permissions / Downstream / Ipc |
| `Status` | `DiagnosticStatus` | Ok / Warn / Fail |
| `Detail` | `string` | Specific info (version, value) |
| `Remediation` | `string?` | How to fix (null if OK) |

**Enums**:
```
DiagnosticCategory { SqlServer, Permissions, Downstream, Ipc }
DiagnosticStatus { Ok, Warn, Fail }
```

## State Store Tables (SQL Server)

### dbo.__ExtractorTableStates

| Column | SQL Type | Constraints |
|--------|----------|-------------|
| `TableSchema` | `nvarchar(128)` | PK part 1 |
| `TableName` | `nvarchar(128)` | PK part 2 |
| `ExtractionMode` | `nvarchar(10)` | NOT NULL, CHECK (CDC, SNAP) |
| `LastProcessedLsn` | `varbinary(10)` | NULL |
| `BootstrapStatus` | `nvarchar(20)` | NOT NULL, CHECK (Pending, Complete, ReBootstrap) |
| `SchemaHash` | `nvarchar(64)` | NULL |
| `CaptureInstance` | `nvarchar(128)` | NULL |
| `LastSyncTime` | `datetimeoffset` | NULL |
| `ErrorMessage` | `nvarchar(2000)` | NULL |
| `UpdatedAt` | `datetimeoffset` | NOT NULL, DEFAULT SYSDATETIMEOFFSET() |

### dbo.__ExtractorBatchHistory

| Column | SQL Type | Constraints |
|--------|----------|-------------|
| `BatchId` | `uniqueidentifier` | PK |
| `RemoteBatchId` | `nvarchar(100)` | NULL |
| `BatchType` | `nvarchar(10)` | NOT NULL |
| `Trigger` | `nvarchar(10)` | NOT NULL |
| `Status` | `nvarchar(10)` | NOT NULL |
| `LeaseToken` | `nvarchar(200)` | NULL |
| `StartedAt` | `datetimeoffset` | NOT NULL |
| `FinishedAt` | `datetimeoffset` | NULL |

### dbo.__ExtractorDatasetHistory

| Column | SQL Type | Constraints |
|--------|----------|-------------|
| `DatasetId` | `uniqueidentifier` | PK |
| `BatchId` | `uniqueidentifier` | FK -> BatchHistory |
| `RemoteDatasetId` | `nvarchar(100)` | NULL |
| `TableSchema` | `nvarchar(128)` | NOT NULL |
| `TableName` | `nvarchar(128)` | NOT NULL |
| `FromLsn` | `varbinary(10)` | NULL |
| `ToLsn` | `varbinary(10)` | NULL |
| `Status` | `nvarchar(10)` | NOT NULL |
| `RowCount` | `bigint` | NOT NULL, DEFAULT 0 |
| `ChunkCount` | `int` | NOT NULL, DEFAULT 0 |
| `BytesSent` | `bigint` | NOT NULL, DEFAULT 0 |
| `ErrorCode` | `nvarchar(50)` | NULL |
| `ErrorMessage` | `nvarchar(2000)` | NULL |

### dbo.__ExtractorConfig

| Column | SQL Type | Constraints |
|--------|----------|-------------|
| `Key` | `nvarchar(255)` | PK |
| `Value` | `nvarchar(max)` | NOT NULL |
| `UpdatedAt` | `datetimeoffset` | NOT NULL |

Note: Tables prefixed with `__Extractor` to avoid name conflicts
with user tables in the same database.

## Domain Events

| Event | Raised when | Payload |
|-------|-------------|---------|
| `BatchStarted` | New batch begins | BatchId, Type, Trigger |
| `BatchFinished` | Batch completes/fails/aborts | BatchId, Status, Duration |
| `DatasetCommitted` | Dataset committed to downstream | DatasetId, TableId, Lsn range, Rows |
| `DatasetFailed` | Dataset upload/commit failed | DatasetId, TableId, ErrorCode |
| `CdcGapDetected` | LSN gap found for a table | TableId, ExpectedLsn, MinAvailableLsn |
| `TableReBootstrapFlagged` | Table marked for re-bootstrap | TableId, Reason |
| `SchemaChanged` | Table schema hash changed | TableId, OldHash, NewHash |
| `PrerequisiteCheckFailed` | Diagnostic check failed | CheckName, Detail, Remediation |

## Interfaces (Repositories / Ports)

```
IStateStore
  GetTableStateAsync(TableIdentifier) -> TableState?
  GetAllTableStatesAsync() -> IReadOnlyList<TableState>
  UpsertTableStateAsync(TableState, IDbTransaction?) -> void
  DeleteTableStateAsync(TableIdentifier) -> void

IBatchHistoryStore
  SaveBatchAsync(BatchRun) -> void
  UpdateBatchStatusAsync(BatchId, BatchStatus, DateTimeOffset?) -> void
  GetRecentBatchesAsync(int limit) -> IReadOnlyList<BatchRun>
  GetBatchByIdAsync(BatchId) -> BatchRun?

IDownstreamClient
  CreateBatchAsync(BatchType, Source) -> (string batchId, string leaseToken)
  HeartbeatAsync(string batchId, string leaseToken) -> void
  FinishBatchAsync(string batchId, string leaseToken, BatchStatus) -> void
  ReportErrorAsync(string batchId, BatchError) -> void
  CreateDatasetAsync(string batchId, DatasetCreateRequest) -> string datasetId
  UploadChunkAsync(string datasetId, int chunkNo, Stream gzipCsv) -> void
  CommitDatasetAsync(string datasetId) -> void
  AbortDatasetAsync(string datasetId) -> void
  UploadSchemaAsync(TableIdentifier, SchemaHash, SchemaManifest) -> void

ICdcReader
  GetMaxLsnAsync() -> Lsn
  GetMinLsnAsync(string captureInstance) -> Lsn
  ReadAllChangesAsync(string captureInstance, Lsn from, Lsn to) -> IAsyncEnumerable<CdcChangeRow>
  ReadFullTableAsync(TableIdentifier) -> IAsyncEnumerable<DataRow>

ICdcManager
  IsCdcEnabledOnDatabaseAsync() -> bool
  EnableCdcOnDatabaseAsync() -> void
  IsCdcEnabledOnTableAsync(TableIdentifier) -> bool
  EnableCdcOnTableAsync(TableIdentifier) -> string captureInstance
  GetRetentionMinutesAsync() -> int
  SetRetentionMinutesAsync(int minutes) -> void
  IsSqlAgentRunningAsync() -> bool

ISchemaInspector
  GetTableMetadataAsync(TableIdentifier) -> SchemaManifest
  GetAllTablesAsync() -> IReadOnlyList<TableDiscoveryInfo>

IDiagnosticsService
  RunAllChecksAsync() -> IReadOnlyList<DiagnosticCheck>

IScheduler
  GetNextRunTimeAsync() -> DateTimeOffset?
  IsRunningAsync() -> bool
  TriggerManualRunAsync() -> void

ITokenProvider
  GetAccessTokenAsync(CancellationToken) -> string
  RefreshAccessTokenAsync(CancellationToken) -> string
```
