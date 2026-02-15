# Downstream API Client Contract

**Base URL**: configurable (e.g., `https://downstream.example.com/v1`)
**Auth**: OAuth 2.0 Bearer token (Device Flow, auto-refreshed)
**Content**: `application/json` for metadata, `application/octet-stream`
(gzip CSV) for chunks

This document defines the HTTP calls the extractor makes as a
client. Full server API spec is in `docs/APIs.md`.

## Batch Lifecycle

### Create Batch

```
POST /v1/batches
Authorization: Bearer {token}
Content-Type: application/json

{
  "source": {
    "sqlServer": "SQLSERVER01\\MAIN",
    "database": "OrdersDB"
  },
  "trigger": "SCHEDULED",
  "type": "DELTA"
}

Response 201:
{
  "batchId": "b-uuid",
  "leaseToken": "lt-uuid"
}
```

### Heartbeat

```
POST /v1/batches/{batchId}:heartbeat
Authorization: Bearer {token}
X-Batch-Lease: {leaseToken}

Response 204 (no content)
Response 409: lease superseded
```

### Finish Batch

```
POST /v1/batches/{batchId}:finish
Authorization: Bearer {token}
X-Batch-Lease: {leaseToken}
Content-Type: application/json

{
  "status": "SUCCEEDED",
  "summary": {
    "tablesTotal": 5,
    "tablesSucceeded": 5,
    "totalRows": 48231,
    "totalBytes": 12345678
  }
}

Response 200
Response 409: lease superseded
```

### Report Error

```
POST /v1/batches/{batchId}/errors
Authorization: Bearer {token}
X-Batch-Lease: {leaseToken}
Content-Type: application/json

{
  "occurredAt": "2026-02-15T08:02:10Z",
  "scope": "TABLE",
  "table": "dbo.Products",
  "datasetId": "ds-uuid",
  "severity": "ERROR",
  "code": "PERMISSION_DENIED",
  "message": "No SELECT permission on cdc.dbo_Products_CT",
  "details": { "user": "svc-extractor" },
  "isRetryable": false,
  "isTerminal": true
}

Response 201
```

## Dataset Lifecycle

### Create Dataset

```
POST /v1/datasets
Authorization: Bearer {token}
X-Batch-Lease: {leaseToken}
Content-Type: application/json

{
  "batchId": "b-uuid",
  "table": "dbo.Orders",
  "fromLsn": "0x00000027000001E80003",
  "toLsn": "0x00000027000002A00001",
  "schemaHash": "sha256-abc123"
}

Response 201:
{
  "datasetId": "ds-uuid"
}
```

### Upload Chunk

```
POST /v1/datasets/{datasetId}/chunks
Authorization: Bearer {token}
X-Batch-Lease: {leaseToken}
Content-Type: application/octet-stream
Content-Encoding: gzip
X-Chunk-No: 3

<gzip CSV binary body>

Response 201
Response 409: lease superseded
```

### Commit Dataset

```
POST /v1/datasets/{datasetId}:commit
Authorization: Bearer {token}
X-Batch-Lease: {leaseToken}

Response 200
Response 409: lease superseded
```

### Abort Dataset

```
POST /v1/datasets/{datasetId}:abort
Authorization: Bearer {token}
X-Batch-Lease: {leaseToken}

Response 200
```

## Schema Resource

### Upload Schema

```
PUT /v1/tables/{table}/schemas/{schemaHash}
Authorization: Bearer {token}
Content-Type: application/json

{
  "table": "dbo.Orders",
  "capturedAt": "2026-02-15T08:00:00Z",
  "schemaHash": "sha256-abc123",
  "columns": [
    {
      "name": "OrderId",
      "sqlType": "int",
      "nullable": false,
      "ordinalPosition": 1
    },
    {
      "name": "CustomerName",
      "sqlType": "nvarchar",
      "nullable": true,
      "maxLength": 200,
      "ordinalPosition": 2
    },
    {
      "name": "Amount",
      "sqlType": "decimal",
      "nullable": false,
      "precision": 18,
      "scale": 2,
      "ordinalPosition": 3
    }
  ],
  "primaryKey": ["OrderId"],
  "uniqueKeys": [],
  "watermarkColumn": null
}

Response 200 (upsert, idempotent by schemaHash)
```

## OAuth Device Flow

### Request Device Code

```
POST /device/authorize
Content-Type: application/x-www-form-urlencoded

client_id={clientId}&scope=extractor

Response 200:
{
  "device_code": "device-uuid",
  "user_code": "ABCD-EFGH",
  "verification_uri": "https://downstream.example.com/device",
  "expires_in": 900,
  "interval": 5
}
```

### Poll for Token

```
POST /token
Content-Type: application/x-www-form-urlencoded

grant_type=urn:ietf:params:oauth:grant-type:device_code
&device_code={deviceCode}
&client_id={clientId}

Response 200 (authorized):
{
  "access_token": "at-xxx",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "rt-xxx"
}

Response 400 (pending):
{ "error": "authorization_pending" }

Response 400 (expired):
{ "error": "expired_token" }
```

### Refresh Token

```
POST /token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token
&refresh_token={refreshToken}
&client_id={clientId}

Response 200:
{
  "access_token": "at-new",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "rt-new"
}
```

## Error Handling

| HTTP Status | Meaning | Retry? |
|-------------|---------|--------|
| 400 | Validation error | No |
| 401 | Auth expired/invalid | Refresh token, retry once |
| 403 | Forbidden | No |
| 404 | Resource not found | No |
| 409 | Lease conflict (superseded) | No (abort batch) |
| 429 | Rate limited | Yes (backoff per Retry-After) |
| 500 | Server error | Yes (transient) |
| 503 | Service unavailable | Yes (transient) |

**Retry policy**: Exponential backoff with jitter.
- Initial delay: 1 second
- Max delay: 30 seconds
- Max retries: 5 for transient errors
- Use Polly `WaitAndRetryAsync` with `DecorrelatedJitterBackoffV2`
