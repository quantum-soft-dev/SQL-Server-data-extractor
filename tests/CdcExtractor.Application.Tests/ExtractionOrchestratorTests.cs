using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CdcExtractor.Application.Tests;

public class ExtractionOrchestratorTests
{
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();
    private readonly IBatchHistoryStore _batchHistoryStore = Substitute.For<IBatchHistoryStore>();
    private readonly IDownstreamClient _downstreamClient = Substitute.For<IDownstreamClient>();
    private readonly ISnapshotService _snapshotService = Substitute.For<ISnapshotService>();
    private readonly IDeltaService _deltaService = Substitute.For<IDeltaService>();
    private readonly ISchemaService _schemaService = Substitute.For<ISchemaService>();
    private readonly ILogger<ExtractionOrchestrator> _logger = Substitute.For<ILogger<ExtractionOrchestrator>>();

    private readonly SqlServerConfig _sqlConfig = new()
    {
        Server = "localhost",
        Database = "TestDb"
    };

    private ExtractionOrchestrator CreateSut() =>
        new(_stateStore, _batchHistoryStore, _downstreamClient,
            _snapshotService, _deltaService, _schemaService, _sqlConfig, _logger);

    private static TableState CreateTableState(string schema, string name) =>
        new(new TableIdentifier(schema, name), ExtractionMode.Snap);

    [Fact]
    public async Task RunSnapshotBatchAsync_CreatesSnapshotBatchInDownstream()
    {
        // Arrange
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableState>());

        _downstreamClient.CreateBatchAsync(
                BatchType.Snapshot, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        var sut = CreateSut();

        // Act
        await sut.RunSnapshotBatchAsync(BatchTrigger.Manual, CancellationToken.None);

        // Assert
        await _downstreamClient.Received(1).CreateBatchAsync(
            BatchType.Snapshot, "localhost", "TestDb", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSnapshotBatchAsync_IteratesAllTablesFromStateStore()
    {
        // Arrange
        var tables = new List<TableState>
        {
            CreateTableState("dbo", "Orders"),
            CreateTableState("dbo", "Customers"),
            CreateTableState("dbo", "Products")
        };

        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        _schemaService.InspectAndUploadSchemaAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SchemaManifest(
                new TableIdentifier("dbo", "Orders"),
                DateTimeOffset.UtcNow,
                new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1"),
                [], [], [], null));

        _snapshotService.ExtractSnapshotAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DatasetRun.Create(callInfo.ArgAt<TableIdentifier>(0), null, null));

        var sut = CreateSut();

        // Act
        await sut.RunSnapshotBatchAsync(BatchTrigger.Manual, CancellationToken.None);

        // Assert
        await _snapshotService.Received(3).ExtractSnapshotAsync(
            Arg.Any<TableIdentifier>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<SchemaManifest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSnapshotBatchAsync_CallsSchemaServicePerTable()
    {
        // Arrange
        var tables = new List<TableState>
        {
            CreateTableState("dbo", "Orders"),
            CreateTableState("dbo", "Customers")
        };

        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        _schemaService.InspectAndUploadSchemaAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SchemaManifest(
                new TableIdentifier("dbo", "Orders"),
                DateTimeOffset.UtcNow,
                new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1"),
                [], [], [], null));

        _snapshotService.ExtractSnapshotAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DatasetRun.Create(callInfo.ArgAt<TableIdentifier>(0), null, null));

        var sut = CreateSut();

        // Act
        await sut.RunSnapshotBatchAsync(BatchTrigger.Manual, CancellationToken.None);

        // Assert
        await _schemaService.Received(2).InspectAndUploadSchemaAsync(
            Arg.Any<TableIdentifier>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSnapshotBatchAsync_FinishesBatchSucceeded_WhenAllTablesSucceed()
    {
        // Arrange
        var tables = new List<TableState>
        {
            CreateTableState("dbo", "Orders"),
            CreateTableState("dbo", "Customers")
        };

        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        _schemaService.InspectAndUploadSchemaAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SchemaManifest(
                new TableIdentifier("dbo", "Orders"),
                DateTimeOffset.UtcNow,
                new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1"),
                [], [], [], null));

        _snapshotService.ExtractSnapshotAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DatasetRun.Create(callInfo.ArgAt<TableIdentifier>(0), null, null));

        var sut = CreateSut();

        // Act
        await sut.RunSnapshotBatchAsync(BatchTrigger.Manual, CancellationToken.None);

        // Assert
        await _downstreamClient.Received(1).FinishBatchAsync(
            Arg.Any<string>(), Arg.Any<string>(), BatchStatus.Succeeded,
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSnapshotBatchAsync_FinishesBatchFailed_WhenATableFails()
    {
        // Arrange
        var tables = new List<TableState>
        {
            CreateTableState("dbo", "Orders"),
            CreateTableState("dbo", "Customers")
        };

        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(tables);

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        _schemaService.InspectAndUploadSchemaAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SchemaManifest(
                new TableIdentifier("dbo", "Orders"),
                DateTimeOffset.UtcNow,
                new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1"),
                [], [], [], null));

        // First table succeeds
        _snapshotService.ExtractSnapshotAsync(
                Arg.Is<TableIdentifier>(t => t.Name == "Orders"),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SchemaManifest>(),
                Arg.Any<CancellationToken>())
            .Returns(DatasetRun.Create(new TableIdentifier("dbo", "Orders"), null, null));

        // Second table fails
        _snapshotService.ExtractSnapshotAsync(
                Arg.Is<TableIdentifier>(t => t.Name == "Customers"),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SchemaManifest>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Snapshot extraction failed"));

        var sut = CreateSut();

        // Act
        await sut.RunSnapshotBatchAsync(BatchTrigger.Manual, CancellationToken.None);

        // Assert
        await _downstreamClient.Received(1).FinishBatchAsync(
            Arg.Any<string>(), Arg.Any<string>(), BatchStatus.Failed,
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    // =======================================================================
    // Re-bootstrap flow tests (T097 — US4)
    // =======================================================================

    private static readonly SchemaHash TestSchemaHash =
        new("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");

    private static SchemaManifest CreateTestManifest(TableIdentifier table) =>
        new(table, DateTimeOffset.UtcNow, TestSchemaHash, [], [], [], null);

    /// <summary>Creates a CDC-mode table in ReBootstrap status (Complete → MarkReBootstrap).</summary>
    private static TableState CreateReBootstrapTableState(string schema, string name)
    {
        var lsn = Lsn.Parse("0x00000027000001E80003");
        var state = new TableState(new TableIdentifier(schema, name), ExtractionMode.Cdc);
        state.SetCaptureInstance($"{schema}_{name}");
        state.MarkComplete(lsn, TestSchemaHash, DateTimeOffset.UtcNow);
        state.MarkReBootstrap("CDC gap detected — history was cleaned before extraction.");
        return state;
    }

    /// <summary>Creates a CDC-mode table in Complete status (ready for delta extraction).</summary>
    private static TableState CreateCdcTableState(string schema, string name)
    {
        var lsn = Lsn.Parse("0x00000027000001E80003");
        var state = new TableState(new TableIdentifier(schema, name), ExtractionMode.Cdc);
        state.SetCaptureInstance($"{schema}_{name}");
        state.MarkComplete(lsn, TestSchemaHash, DateTimeOffset.UtcNow);
        return state;
    }

    private void SetupCommonDeltaBatchMocks()
    {
        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        _schemaService.InspectAndUploadSchemaAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => CreateTestManifest(callInfo.ArgAt<TableIdentifier>(0)));
    }

    // -----------------------------------------------------------------------
    // Test: ReBootstrap table routed to SnapshotService in delta batch
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RunDeltaBatchAsync_ReBootstrapTable_RoutesToSnapshotService()
    {
        // Arrange — one table in ReBootstrap status
        var rebootstrapTable = CreateReBootstrapTableState("dbo", "Orders");
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableState> { rebootstrapTable });

        SetupCommonDeltaBatchMocks();

        _snapshotService.ExtractSnapshotAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DatasetRun.Create(callInfo.ArgAt<TableIdentifier>(0), null, null));

        var sut = CreateSut();

        // Act
        await sut.RunDeltaBatchAsync(BatchTrigger.Scheduled, CancellationToken.None);

        // Assert — SnapshotService called (not DeltaService)
        await _snapshotService.Received(1).ExtractSnapshotAsync(
            rebootstrapTable.TableId,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SchemaManifest>(),
            Arg.Any<CancellationToken>());

        await _deltaService.DidNotReceive().ExtractDeltaAsync(
            Arg.Any<TableState>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test: ReBootstrap table — CDC_GAP_DETECTED error reported to downstream
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RunDeltaBatchAsync_ReBootstrapTable_ReportsGapErrorToDownstream()
    {
        // Arrange
        var rebootstrapTable = CreateReBootstrapTableState("dbo", "Orders");
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableState> { rebootstrapTable });

        SetupCommonDeltaBatchMocks();

        _snapshotService.ExtractSnapshotAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DatasetRun.Create(callInfo.ArgAt<TableIdentifier>(0), null, null));

        var sut = CreateSut();

        // Act
        await sut.RunDeltaBatchAsync(BatchTrigger.Scheduled, CancellationToken.None);

        // Assert — CDC_GAP_DETECTED error reported
        await _downstreamClient.Received(1).ReportErrorAsync(
            "batch-1", "lease-abc", "TABLE", rebootstrapTable.TableId.FullName,
            Arg.Any<string?>(), "ERROR", "CDC_GAP_DETECTED",
            Arg.Is<string>(m => m.Contains(rebootstrapTable.TableId.FullName)),
            Arg.Is(false), Arg.Is(true),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test: ReBootstrap table — status reset to Complete after successful snapshot
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RunDeltaBatchAsync_ReBootstrapTable_ResetsStatusToComplete()
    {
        // Arrange
        var rebootstrapTable = CreateReBootstrapTableState("dbo", "Orders");
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableState> { rebootstrapTable });

        SetupCommonDeltaBatchMocks();

        _snapshotService.ExtractSnapshotAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DatasetRun.Create(callInfo.ArgAt<TableIdentifier>(0), null, null));

        var sut = CreateSut();

        // Act
        await sut.RunDeltaBatchAsync(BatchTrigger.Scheduled, CancellationToken.None);

        // Assert — SnapshotService calls MarkComplete internally, which resets status.
        // The orchestrator should persist the updated state.
        await _stateStore.Received().UpsertTableStateAsync(
            Arg.Is<TableState>(t => t.TableId == rebootstrapTable.TableId),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test: Mixed batch — ReBootstrap goes to snapshot, Complete goes to delta
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RunDeltaBatchAsync_MixedTables_RoutesCorrectly()
    {
        // Arrange
        var rebootstrapTable = CreateReBootstrapTableState("dbo", "Orders");
        var normalTable = CreateCdcTableState("dbo", "Products");

        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableState> { rebootstrapTable, normalTable });

        SetupCommonDeltaBatchMocks();

        _snapshotService.ExtractSnapshotAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DatasetRun.Create(callInfo.ArgAt<TableIdentifier>(0), null, null));

        _deltaService.ExtractDeltaAsync(
                Arg.Any<TableState>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DatasetRun.Create(callInfo.ArgAt<TableState>(0).TableId, null, null));

        var sut = CreateSut();

        // Act
        await sut.RunDeltaBatchAsync(BatchTrigger.Scheduled, CancellationToken.None);

        // Assert — ReBootstrap table routed to SnapshotService
        await _snapshotService.Received(1).ExtractSnapshotAsync(
            rebootstrapTable.TableId,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SchemaManifest>(),
            Arg.Any<CancellationToken>());

        // Assert — Normal CDC table routed to DeltaService
        await _deltaService.Received(1).ExtractDeltaAsync(
            normalTable,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SchemaManifest>(),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test: ReBootstrap snapshot failure — batch still FAILED, table error set
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RunDeltaBatchAsync_ReBootstrapSnapshotFails_SetsBatchFailed()
    {
        // Arrange
        var rebootstrapTable = CreateReBootstrapTableState("dbo", "Orders");
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableState> { rebootstrapTable });

        SetupCommonDeltaBatchMocks();

        _snapshotService.ExtractSnapshotAsync(
                Arg.Any<TableIdentifier>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<SchemaManifest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Snapshot failed during re-bootstrap"));

        var sut = CreateSut();

        // Act
        var result = await sut.RunDeltaBatchAsync(BatchTrigger.Scheduled, CancellationToken.None);

        // Assert
        await _downstreamClient.Received(1).FinishBatchAsync(
            Arg.Any<string>(), Arg.Any<string>(), BatchStatus.Failed,
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }
}
