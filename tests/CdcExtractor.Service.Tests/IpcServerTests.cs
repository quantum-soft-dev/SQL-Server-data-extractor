using System.Diagnostics;
using System.Runtime.Versioning;
using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Contracts.Ipc;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.Models;
using CdcExtractor.Domain.ValueObjects;
using CdcExtractor.Service.Ipc;
using CdcExtractor.Service.Workers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CdcExtractor.Service.Tests;

[SupportedOSPlatform("windows")]
public class IpcServerTests
{
    private readonly IBatchHistoryStore _batchHistoryStore = Substitute.For<IBatchHistoryStore>();
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();
    private readonly IDiagnosticsService _diagnosticsService = Substitute.For<IDiagnosticsService>();
    private readonly LogBroadcaster _logBroadcaster = new();

    private SchedulerWorker CreateSchedulerWorker(ScheduleConfig? config = null)
    {
        var orchestrator = new ExtractionOrchestrator(
            Substitute.For<IStateStore>(),
            Substitute.For<IBatchHistoryStore>(),
            Substitute.For<IDownstreamClient>(),
            Substitute.For<ISnapshotService>(),
            Substitute.For<IDeltaService>(),
            Substitute.For<ISchemaService>(),
            new SqlServerConfig { Server = "localhost", Database = "TestDb" },
            NullLogger<ExtractionOrchestrator>.Instance);

        return new SchedulerWorker(
            orchestrator,
            config ?? new ScheduleConfig { CronExpressions = [], Timezone = "UTC" },
            NullLogger<SchedulerWorker>.Instance);
    }

    private ExtractorServiceRpc CreateSut(SchedulerWorker? schedulerWorker = null) =>
        new(_batchHistoryStore, _stateStore, _diagnosticsService,
            _logBroadcaster, schedulerWorker ?? CreateSchedulerWorker());

    // ---------------------------------------------------------------
    // GetStatusAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetStatusAsync_ReturnsIsRunningFalse_WhenNoBatchRunning()
    {
        var sut = CreateSut();

        var result = await sut.GetStatusAsync();

        result.IsRunning.Should().BeFalse();
        result.ServicePid.Should().Be(Environment.ProcessId);
        result.CurrentBatchId.Should().BeNull();
        result.CurrentBatchType.Should().BeNull();
        result.CurrentBatchTrigger.Should().BeNull();
        result.CurrentBatchStartedAt.Should().BeNull();
        result.NextScheduledRun.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsValidUptimeFormat()
    {
        var sut = CreateSut();

        var result = await sut.GetStatusAsync();

        // Uptime format should be ISO 8601 duration-like: PT{hours}H{minutes}M
        result.ServiceUptime.Should().StartWith("PT");
        result.ServiceUptime.Should().Contain("H");
        result.ServiceUptime.Should().EndWith("M");
    }

    // ---------------------------------------------------------------
    // GetBatchProgressAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetBatchProgressAsync_ReturnsEmptyProgress_WhenNoBatchRunning()
    {
        var sut = CreateSut();

        var result = await sut.GetBatchProgressAsync();

        result.BatchId.Should().BeNull();
        result.Tables.Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // GetRecentBatchesAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetRecentBatchesAsync_MapsEntitiesCorrectly()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Snapshot, BatchTrigger.Manual);
        var tableId = new TableIdentifier("dbo", "Orders");
        var fromLsn = Lsn.Parse("0x00000027000001E80003");
        var toLsn = Lsn.Parse("0x00000028000001E80003");
        var dataset = DatasetRun.Create(tableId, fromLsn, toLsn);
        dataset.IncrementRows(42);
        dataset.Commit();
        batch.AddDataset(dataset);
        batch.Finish(BatchStatus.Succeeded);

        _batchHistoryStore.GetRecentBatchesAsync(10, Arg.Any<CancellationToken>())
            .Returns(new List<BatchRun> { batch });

        var sut = CreateSut();

        // Act
        var result = await sut.GetRecentBatchesAsync(10);

        // Assert
        result.Batches.Should().HaveCount(1);
        var summary = result.Batches[0];
        summary.BatchId.Should().Be(batch.Id.ToString());
        summary.Type.Should().Be("SNAPSHOT");
        summary.Trigger.Should().Be("MANUAL");
        summary.Status.Should().Be("SUCCEEDED");
        summary.TablesTotal.Should().Be(1);
        summary.TablesSucceeded.Should().Be(1);
        summary.TotalRows.Should().Be(42);
        summary.StartedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        summary.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRecentBatchesAsync_ReturnsEmptyList_WhenNoHistory()
    {
        _batchHistoryStore.GetRecentBatchesAsync(5, Arg.Any<CancellationToken>())
            .Returns(new List<BatchRun>());

        var sut = CreateSut();

        var result = await sut.GetRecentBatchesAsync(5);

        result.Batches.Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // GetBatchDetailsAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetBatchDetailsAsync_MapsBatchWithDatasets()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Delta, BatchTrigger.Scheduled);
        var tableId = new TableIdentifier("dbo", "Customers");
        var fromLsn = Lsn.Parse("0x00000001000000010001");
        var toLsn = Lsn.Parse("0x00000002000000020002");
        var dataset = DatasetRun.Create(tableId, fromLsn, toLsn);
        dataset.IncrementRows(100);
        dataset.IncrementChunk(2048);
        dataset.IncrementChunk(1024);
        dataset.Commit();
        batch.AddDataset(dataset);

        var failedDataset = DatasetRun.Create(
            new TableIdentifier("dbo", "Products"),
            Lsn.Parse("0x00000003000000030003"),
            Lsn.Parse("0x00000004000000040004"));
        failedDataset.Abort("CDC_GAP", "Gap detected in CDC log");
        batch.AddDataset(failedDataset);

        batch.Finish(BatchStatus.Failed);

        _batchHistoryStore.GetBatchByIdAsync(Arg.Any<BatchId>(), Arg.Any<CancellationToken>())
            .Returns(batch);

        var sut = CreateSut();

        // Act
        var result = await sut.GetBatchDetailsAsync(batch.Id.ToString());

        // Assert
        result.BatchId.Should().Be(batch.Id.ToString());
        result.Type.Should().Be("DELTA");
        result.Trigger.Should().Be("SCHEDULED");
        result.Status.Should().Be("FAILED");
        result.FinishedAt.Should().NotBeNull();

        result.Datasets.Should().HaveCount(2);

        var committedDs = result.Datasets[0];
        committedDs.Table.Should().Be("dbo.Customers");
        committedDs.FromLsn.Should().Be(fromLsn.ToString());
        committedDs.ToLsn.Should().Be(toLsn.ToString());
        committedDs.Status.Should().Be("COMMITTED");
        committedDs.RowCount.Should().Be(100);
        committedDs.ChunkCount.Should().Be(2);
        committedDs.ErrorCode.Should().BeNull();
        committedDs.ErrorMessage.Should().BeNull();

        var failedDs = result.Datasets[1];
        failedDs.Table.Should().Be("dbo.Products");
        failedDs.Status.Should().Be("ABORTED");
        failedDs.ErrorCode.Should().Be("CDC_GAP");
        failedDs.ErrorMessage.Should().Be("Gap detected in CDC log");
    }

    [Fact]
    public async Task GetBatchDetailsAsync_ReturnsEmptyDetails_WhenBatchNotFound()
    {
        var batchIdString = Guid.NewGuid().ToString();

        _batchHistoryStore.GetBatchByIdAsync(Arg.Any<BatchId>(), Arg.Any<CancellationToken>())
            .Returns((BatchRun?)null);

        var sut = CreateSut();

        var result = await sut.GetBatchDetailsAsync(batchIdString);

        result.BatchId.Should().Be(batchIdString);
        result.Type.Should().BeEmpty();
        result.Trigger.Should().BeEmpty();
        result.Status.Should().BeEmpty();
        result.Datasets.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // GetTableStatesAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetTableStatesAsync_MapsTableStatesToDtos()
    {
        // Arrange
        var tableId1 = new TableIdentifier("dbo", "Orders");
        var state1 = new TableState(tableId1, ExtractionMode.Cdc);
        state1.SetCaptureInstance("dbo_Orders");
        var schemaHash = SchemaHash.Compute(new { Col1 = "int", Col2 = "varchar" });
        var syncTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        state1.MarkComplete(Lsn.Parse("0x00000027000001E80003"), schemaHash, syncTime);

        var tableId2 = new TableIdentifier("dbo", "Products");
        var state2 = new TableState(tableId2, ExtractionMode.Snap);
        // state2 stays in Pending status, no CaptureInstance

        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TableState> { state1, state2 });

        var sut = CreateSut();

        // Act
        var result = await sut.GetTableStatesAsync();

        // Assert
        result.Tables.Should().HaveCount(2);

        var dto1 = result.Tables[0];
        dto1.Table.Should().Be("dbo.Orders");
        dto1.Mode.Should().Be("CDC");
        dto1.CdcEnabled.Should().BeTrue();
        dto1.LastProcessedLsn.Should().Be("0x00000027000001E80003");
        dto1.BootstrapStatus.Should().Be("COMPLETE");
        dto1.LastSyncTime.Should().Be(syncTime);
        dto1.Lag.Should().BeGreaterThanOrEqualTo(9); // at least 9 minutes of lag
        dto1.Error.Should().BeNull();

        var dto2 = result.Tables[1];
        dto2.Table.Should().Be("dbo.Products");
        dto2.Mode.Should().Be("SNAP");
        dto2.CdcEnabled.Should().BeFalse();
        dto2.BootstrapStatus.Should().Be("PENDING");
        dto2.LastSyncTime.Should().BeNull();
        dto2.Lag.Should().Be(0);
        dto2.Error.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // RunDiagnosticsAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task RunDiagnosticsAsync_CallsServiceAndMapsDiagnosticChecks()
    {
        // Arrange
        var checks = new List<DiagnosticCheck>
        {
            new("SqlServerConnection", DiagnosticCategory.SqlServer, DiagnosticStatus.Ok,
                "Connected to localhost", null),
            new("CdcEnabled", DiagnosticCategory.Permissions, DiagnosticStatus.Warn,
                "CDC not enabled on table dbo.Products",
                "Run sys.sp_cdc_enable_table for the table"),
            new("DownstreamReachable", DiagnosticCategory.Downstream, DiagnosticStatus.Fail,
                "Cannot reach downstream API at https://api.example.com",
                "Check network connectivity and API credentials")
        };

        _diagnosticsService.RunAllChecksAsync(Arg.Any<CancellationToken>())
            .Returns(checks);

        var sut = CreateSut();

        // Act
        var result = await sut.RunDiagnosticsAsync();

        // Assert
        await _diagnosticsService.Received(1).RunAllChecksAsync(Arg.Any<CancellationToken>());

        result.Checks.Should().HaveCount(3);

        var okCheck = result.Checks[0];
        okCheck.Name.Should().Be("SqlServerConnection");
        okCheck.Category.Should().Be("SqlServer");
        okCheck.Status.Should().Be("OK");
        okCheck.Detail.Should().Be("Connected to localhost");
        okCheck.Remediation.Should().BeNull();

        var warnCheck = result.Checks[1];
        warnCheck.Name.Should().Be("CdcEnabled");
        warnCheck.Category.Should().Be("Permissions");
        warnCheck.Status.Should().Be("WARN");
        warnCheck.Remediation.Should().Be("Run sys.sp_cdc_enable_table for the table");

        var failCheck = result.Checks[2];
        failCheck.Name.Should().Be("DownstreamReachable");
        failCheck.Category.Should().Be("Downstream");
        failCheck.Status.Should().Be("FAIL");
        failCheck.Remediation.Should().NotBeNull();
    }

    // ---------------------------------------------------------------
    // Constructor — null-guard tests
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_ThrowsArgumentNullException_ForNullDependencies()
    {
        var logBroadcaster = _logBroadcaster;
        var schedulerWorker = CreateSchedulerWorker();

        var act1 = () => new ExtractorServiceRpc(null!, _stateStore, _diagnosticsService, logBroadcaster, schedulerWorker);
        var act2 = () => new ExtractorServiceRpc(_batchHistoryStore, null!, _diagnosticsService, logBroadcaster, schedulerWorker);
        var act3 = () => new ExtractorServiceRpc(_batchHistoryStore, _stateStore, null!, logBroadcaster, schedulerWorker);
        var act4 = () => new ExtractorServiceRpc(_batchHistoryStore, _stateStore, _diagnosticsService, null!, schedulerWorker);
        var act5 = () => new ExtractorServiceRpc(_batchHistoryStore, _stateStore, _diagnosticsService, logBroadcaster, null!);

        act1.Should().Throw<ArgumentNullException>().WithParameterName("batchHistoryStore");
        act2.Should().Throw<ArgumentNullException>().WithParameterName("stateStore");
        act3.Should().Throw<ArgumentNullException>().WithParameterName("diagnosticsService");
        act4.Should().Throw<ArgumentNullException>().WithParameterName("logBroadcaster");
        act5.Should().Throw<ArgumentNullException>().WithParameterName("schedulerWorker");
    }
}
