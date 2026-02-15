# IPC Contract: Named Pipes + JSON-RPC (StreamJsonRpc)

**Transport**: Windows Named Pipes
**Pipe name**: `SQLExtractorIPC`
**Protocol**: JSON-RPC 2.0 via StreamJsonRpc
**Direction**: Bidirectional (client = WPF App, server = Windows Service)

## Methods (Client -> Server)

### getStatus

Returns current service status.

**Request**: `{}` (no params)
**Response**:
```json
{
  "isRunning": true,
  "currentBatchId": "abc-123",
  "currentBatchType": "DELTA",
  "currentBatchTrigger": "SCHEDULED",
  "currentBatchStartedAt": "2026-02-15T08:00:00+03:00",
  "nextScheduledRun": "2026-02-15T12:00:00+03:00",
  "serviceUptime": "PT4H12M",
  "servicePid": 12482
}
```

### getBatchProgress

Returns per-table progress for the current batch.

**Request**: `{}` (no params)
**Response**:
```json
{
  "batchId": "abc-123",
  "tables": [
    {
      "table": "dbo.Orders",
      "status": "UPLOADING",
      "progressPercent": 88,
      "rowsProcessed": 44000,
      "rowsTotal": 50000,
      "chunksUploaded": 7,
      "chunksTotal": 9
    }
  ]
}
```

### getRecentBatches

Returns recent batch history.

**Request**: `{ "limit": 20 }`
**Response**:
```json
{
  "batches": [
    {
      "batchId": "abc-123",
      "type": "DELTA",
      "trigger": "SCHEDULED",
      "status": "SUCCEEDED",
      "startedAt": "2026-02-15T08:00:00+03:00",
      "finishedAt": "2026-02-15T08:07:12+03:00",
      "tablesTotal": 5,
      "tablesSucceeded": 5,
      "totalRows": 48231
    }
  ]
}
```

### getBatchDetails

Returns details for a specific batch.

**Request**: `{ "batchId": "abc-123" }`
**Response**:
```json
{
  "batchId": "abc-123",
  "type": "DELTA",
  "trigger": "SCHEDULED",
  "status": "FAILED",
  "startedAt": "...",
  "finishedAt": "...",
  "datasets": [
    {
      "table": "dbo.Orders",
      "fromLsn": "0x00000027000001E80003",
      "toLsn": "0x00000027000002A00001",
      "status": "COMMITTED",
      "rowCount": 1234,
      "chunkCount": 3
    },
    {
      "table": "dbo.Products",
      "status": "SKIPPED",
      "errorCode": "PERMISSION_DENIED",
      "errorMessage": "No SELECT on cdc.dbo_Products_CT"
    }
  ],
  "errors": [
    {
      "occurredAt": "...",
      "scope": "TABLE",
      "table": "dbo.Products",
      "severity": "ERROR",
      "code": "PERMISSION_DENIED",
      "message": "No SELECT permission on cdc.dbo_Products_CT. Grant SELECT to the service account."
    }
  ]
}
```

### getTableStates

Returns all tracked table states.

**Request**: `{}` (no params)
**Response**:
```json
{
  "tables": [
    {
      "table": "dbo.Orders",
      "mode": "CDC",
      "cdcEnabled": true,
      "lastProcessedLsn": "0x00000027000002A00001",
      "bootstrapStatus": "COMPLETE",
      "lastSyncTime": "2026-02-15T08:07:12+03:00",
      "lag": 0,
      "error": null
    }
  ]
}
```

### triggerRun

Triggers a manual batch run.

**Request**: `{}` (no params)
**Response**:
```json
{
  "accepted": true,
  "batchId": "new-batch-id"
}
```
**Error**: `{ "accepted": false, "reason": "A batch is already running" }`

### runDiagnostics

Runs all diagnostic checks.

**Request**: `{}` (no params)
**Response**:
```json
{
  "checks": [
    {
      "name": "SQL Server connectivity",
      "category": "SqlServer",
      "status": "OK",
      "detail": "SQLSERVER01\\MAIN — SQL Server 2019",
      "remediation": null
    },
    {
      "name": "SELECT on CDC tables",
      "category": "Permissions",
      "status": "FAIL",
      "detail": "dbo.Products: missing SELECT on cdc.dbo_Products_CT",
      "remediation": "GRANT SELECT ON [cdc].[dbo_Products_CT] TO [svc-extractor]"
    }
  ]
}
```

### subscribeLogs

Subscribe to live log stream. After calling this method, the
server starts sending `onLogEntry` notifications.

**Request**: `{ "minLevel": "Information" }`
**Response**: `{ "subscribed": true }`

### unsubscribeLogs

Stop receiving log notifications.

**Request**: `{}` (no params)
**Response**: `{ "unsubscribed": true }`

### getRecentLogs

Get last N log entries (for initial backfill).

**Request**: `{ "count": 200, "minLevel": "Information" }`
**Response**:
```json
{
  "entries": [
    {
      "timestamp": "2026-02-15T08:00:01.234+03:00",
      "level": "Information",
      "message": "Starting DELTA batch run_0215_0800_a1",
      "correlationId": "abc-123",
      "batchId": "abc-123",
      "table": null,
      "properties": {}
    }
  ]
}
```

## Notifications (Server -> Client)

### onLogEntry

Sent for each new log entry while subscribed.

```json
{
  "timestamp": "2026-02-15T08:02:06.789+03:00",
  "level": "Information",
  "message": "Uploaded chunk 3/9 for table dbo.Orders (dataset ds-456). Size: 2.1 MB",
  "correlationId": "abc-123",
  "batchId": "abc-123",
  "table": "dbo.Orders",
  "datasetId": "ds-456",
  "properties": {
    "chunkNo": 3,
    "chunksTotal": 9,
    "bytes": 2202009
  }
}
```

### onBatchProgress

Sent periodically during an active batch (every ~2 seconds).

```json
{
  "batchId": "abc-123",
  "tables": [
    {
      "table": "dbo.Orders",
      "status": "UPLOADING",
      "progressPercent": 91
    }
  ]
}
```

### onBatchFinished

Sent when a batch completes.

```json
{
  "batchId": "abc-123",
  "status": "SUCCEEDED",
  "finishedAt": "2026-02-15T08:07:12+03:00",
  "tablesSucceeded": 5,
  "tablesFailed": 0
}
```

## Security

- Pipe ACL: restricted to the Windows account/group running the
  WPF app and the service account.
- No authentication in protocol (pipe ACL is the security boundary).
- No encryption in protocol (Named Pipes are local-only by default).
