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
    private readonly ISchemaService _schemaService = Substitute.For<ISchemaService>();
    private readonly ILogger<ExtractionOrchestrator> _logger = Substitute.For<ILogger<ExtractionOrchestrator>>();

    private readonly SqlServerConfig _sqlConfig = new()
    {
        Server = "localhost",
        Database = "TestDb"
    };

    private ExtractionOrchestrator CreateSut() =>
        new(_stateStore, _batchHistoryStore, _downstreamClient,
            _snapshotService, _schemaService, _sqlConfig, _logger);

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
}
