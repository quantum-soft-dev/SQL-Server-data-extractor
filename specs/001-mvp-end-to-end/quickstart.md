# Quickstart: MVP End-to-End CDC Data Extractor

**Branch**: `001-mvp-end-to-end`

## Prerequisites

- Windows 10/11 or Windows Server 2016+
- .NET 8 SDK (for building)
- SQL Server 2016+ (Enterprise, Standard, or Developer) with:
  - SQL Agent running
  - A database with tables to extract
  - A login with `db_owner` or `cdc_admin` + SELECT rights
- A running downstream ingestion service (see `docs/APIs.md`)

## Build

```powershell
# Clone and build
git clone <repo-url>
cd SQL-Server-data-extractor
dotnet build src/CdcExtractor.sln

# Run tests
dotnet test tests/CdcExtractor.sln
```

## Project Layout

```
src/
├── CdcExtractor.Domain/           # Domain models, value objects, interfaces, events
├── CdcExtractor.Application/      # Use cases, application services (orchestration)
├── CdcExtractor.Infrastructure/   # SQL Server access, HTTP client, state store
├── CdcExtractor.Contracts/        # Shared DTOs, IPC method contracts
├── CdcExtractor.Service/          # Windows Service host, scheduler, IPC server
└── CdcExtractor.App/              # WPF application (configurator + manager)

tests/
├── CdcExtractor.Domain.Tests/
├── CdcExtractor.Application.Tests/
├── CdcExtractor.Infrastructure.Tests/  # Integration tests (Testcontainers)
├── CdcExtractor.Service.Tests/
└── CdcExtractor.App.Tests/
```

## Run the Service (Development)

```powershell
# Run as console (not as Windows Service) for development
dotnet run --project src/CdcExtractor.Service -- --console

# The service reads config from:
#   %ProgramData%\SQLExtractor\config.json
# Or override via:
#   --config path/to/config.json
```

## Run the WPF App

```powershell
dotnet run --project src/CdcExtractor.App
```

On first launch, the Configurator Wizard opens. Follow the 9 steps
to connect to SQL Server, authorize downstream, select tables,
configure CDC, and optionally run the initial snapshot.

## Install as Windows Service

```powershell
# Publish
dotnet publish src/CdcExtractor.Service -c Release -o publish/service

# Install (requires admin)
sc.exe create SQLExtractorService `
  binPath="C:\path\to\publish\service\CdcExtractor.Service.exe" `
  start=auto `
  DisplayName="SQL Server CDC Data Extractor"

sc.exe start SQLExtractorService
```

## Verify

1. Open the WPF App -> Manager mode.
2. Dashboard should show "Service: Running".
3. Click "Run Now" to trigger a manual batch.
4. Watch live logs and per-table progress.
5. Check Runs screen for batch result.

## Configuration (config.json)

```json
{
  "sqlServer": {
    "server": "SQLSERVER01",
    "instance": "MAIN",
    "database": "OrdersDB",
    "authType": "WindowsAd",
    "encrypt": true
  },
  "downstream": {
    "baseUrl": "https://downstream.example.com/v1",
    "clientId": "extractor-client",
    "heartbeatIntervalSeconds": 120
  },
  "schedule": {
    "cronExpressions": [
      "0 0 8 * * *",
      "0 0 12 * * *",
      "0 0 18 * * *"
    ],
    "timezone": "Europe/Moscow"
  },
  "cdc": {
    "autoEnableDatabase": true,
    "autoEnableTables": true,
    "retentionMinDays": 7,
    "batchInactivityTtlMinutes": 10
  },
  "extraction": {
    "maxBytesPerChunk": 10485760,
    "smallTableSnapThreshold": 1000
  },
  "stateTables": {
    "schema": "dbo",
    "prefix": "__Extractor"
  },
  "logging": {
    "level": "Information",
    "filePath": "C:\\ProgramData\\SQLExtractor\\logs\\extractor-.log",
    "rollingInterval": "Day"
  }
}
```

## Key Flows

### Initial Setup (via Wizard)
1. Welcome -> Connect SQL -> Downstream Auth -> Select Tables
2. CDC & Retention -> Schedule -> Review & Apply
3. (Optional) Run initial SNAPSHOT

### Scheduled DELTA Run
1. Scheduler fires at configured time
2. Service creates DELTA batch in downstream
3. For each table: read CDC changes -> upload chunks -> commit dataset
4. Advance `last_processed_lsn` per table
5. Finish batch (SUCCEEDED/FAILED)

### CDC Gap Recovery
1. Service detects `last_processed_lsn` < min available CDC LSN
2. Table flagged as RE_BOOTSTRAP
3. Error reported to downstream + logged
4. On next SNAPSHOT batch: full re-extract of flagged table
5. New LSN baseline established
