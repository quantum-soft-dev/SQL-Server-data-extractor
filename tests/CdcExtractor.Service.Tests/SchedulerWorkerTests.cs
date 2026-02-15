using System.Runtime.Versioning;
using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Service.Workers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CdcExtractor.Service.Tests;

[SupportedOSPlatform("windows")]
public class SchedulerWorkerTests
{
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();
    private readonly IBatchHistoryStore _batchHistoryStore = Substitute.For<IBatchHistoryStore>();
    private readonly IDownstreamClient _downstreamClient = Substitute.For<IDownstreamClient>();
    private readonly ISnapshotService _snapshotService = Substitute.For<ISnapshotService>();
    private readonly IDeltaService _deltaService = Substitute.For<IDeltaService>();
    private readonly ISchemaService _schemaService = Substitute.For<ISchemaService>();
    private readonly SqlServerConfig _sqlConfig = new() { Server = "localhost", Database = "TestDb" };

    private ExtractionOrchestrator CreateOrchestrator() =>
        new(_stateStore, _batchHistoryStore, _downstreamClient,
            _snapshotService, _deltaService, _schemaService, _sqlConfig,
            NullLogger<ExtractionOrchestrator>.Instance);

    private SchedulerWorker CreateSut(ScheduleConfig? scheduleConfig = null)
    {
        var config = scheduleConfig ?? new ScheduleConfig
        {
            CronExpressions = ["0 */5 * * * *"], // every 5 minutes (6-field)
            Timezone = "UTC",
            PreventParallelRuns = true
        };

        return new SchedulerWorker(
            CreateOrchestrator(),
            config,
            NullLogger<SchedulerWorker>.Instance);
    }

    // ---------------------------------------------------------------
    // GetNextRunTime — cron calculation tests (static method)
    // ---------------------------------------------------------------

    [Fact]
    public void GetNextRunTime_ReturnsEarliestFromAllCronExpressions()
    {
        // Arrange — two cron expressions: every 30 min and every hour (6-field)
        var cronExpressions = new[] { "0 */30 * * * *", "0 0 * * * *" };
        var now = new DateTimeOffset(2026, 2, 15, 10, 1, 0, TimeSpan.Zero);

        // Act
        var next = SchedulerWorker.CalculateNextRunTime(cronExpressions, "UTC", now);

        // Assert — next occurrence of "*/30 * * * *" after 10:01 is 10:30
        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTimeOffset(2026, 2, 15, 10, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextRunTime_ReturnsNull_WhenNoCronExpressions()
    {
        var next = SchedulerWorker.CalculateNextRunTime(
            Array.Empty<string>(), "UTC", DateTimeOffset.UtcNow);

        next.Should().BeNull();
    }

    [Fact]
    public void GetNextRunTime_PicksClosestFromThreeExpressions()
    {
        // "0 0 12 * * *"    = daily at noon
        // "0 */10 * * * *"  = every 10 minutes
        // "0 0 0 * * 1"     = every Monday at midnight
        var cronExpressions = new[] { "0 0 12 * * *", "0 */10 * * * *", "0 0 0 * * 1" };
        var now = new DateTimeOffset(2026, 2, 14, 11, 55, 0, TimeSpan.Zero);

        var next = SchedulerWorker.CalculateNextRunTime(cronExpressions, "UTC", now);

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTimeOffset(2026, 2, 14, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextRunTime_RespectsTimezone()
    {
        // Cron fires at 08:00 daily; timezone is US Eastern (UTC-5 in Feb)
        var cronExpressions = new[] { "0 0 8 * * *" };
        // "now" in UTC is 2026-02-15 12:00 UTC, which is 07:00 EST
        var now = new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);

        var next = SchedulerWorker.CalculateNextRunTime(
            cronExpressions, "America/New_York", now);

        // next 08:00 EST = 13:00 UTC
        next.Should().NotBeNull();
        next!.Value.UtcDateTime.Hour.Should().Be(13);
        next.Value.UtcDateTime.Date.Should().Be(new DateTime(2026, 2, 15));
    }

    // ---------------------------------------------------------------
    // ExecuteAsync — cancellation
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        var config = new ScheduleConfig
        {
            CronExpressions = ["0 0 0 1 1 *"], // once a year
            Timezone = "UTC",
            PreventParallelRuns = true
        };
        var sut = CreateSut(config);

        using var cts = new CancellationTokenSource();

        var workerTask = sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();

        await Task.Delay(500);
        await sut.StopAsync(CancellationToken.None);

        workerTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StopsGracefully_WhenNoCronExpressions()
    {
        var config = new ScheduleConfig
        {
            CronExpressions = [],
            Timezone = "UTC",
            PreventParallelRuns = true
        };
        var sut = CreateSut(config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await sut.StartAsync(cts.Token);
        await Task.Delay(500);
        await sut.StopAsync(CancellationToken.None);

        sut.Should().NotBeNull();
    }

    // ---------------------------------------------------------------
    // TriggerManualRunAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task TriggerManualRunAsync_Accepted_WhenNotRunning()
    {
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Domain.Entities.TableState>());

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        var sut = CreateSut();

        var (accepted, batchId, reason) = await sut.TriggerManualRunAsync(CancellationToken.None);

        accepted.Should().BeTrue();
        batchId.Should().NotBeNullOrWhiteSpace();
        reason.Should().BeNull();
    }

    [Fact]
    public async Task TriggerManualRunAsync_Rejected_WhenBatchRunning()
    {
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(5000, callInfo.Arg<CancellationToken>());
                return (IReadOnlyList<Domain.Entities.TableState>)new List<Domain.Entities.TableState>();
            });

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        var sut = CreateSut();

        _ = sut.TriggerManualRunAsync(CancellationToken.None);
        await Task.Delay(200);

        var (accepted, batchId, reason) = await sut.TriggerManualRunAsync(CancellationToken.None);

        accepted.Should().BeFalse();
        batchId.Should().BeNull();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    // ---------------------------------------------------------------
    // IsRunning property
    // ---------------------------------------------------------------

    [Fact]
    public void IsRunning_ReturnsFalse_WhenIdle()
    {
        var sut = CreateSut();
        sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task IsRunning_ReturnsTrue_DuringBatchExecution()
    {
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(5000, callInfo.Arg<CancellationToken>());
                return (IReadOnlyList<Domain.Entities.TableState>)new List<Domain.Entities.TableState>();
            });

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        var sut = CreateSut();

        _ = sut.TriggerManualRunAsync(CancellationToken.None);
        await Task.Delay(200);

        sut.IsRunning.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // SingleInstanceLock — concurrency
    // ---------------------------------------------------------------

    [Fact]
    public async Task SingleInstanceLock_PreventsParallelRuns()
    {
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(1000, callInfo.Arg<CancellationToken>());
                return (IReadOnlyList<Domain.Entities.TableState>)new List<Domain.Entities.TableState>();
            });

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        var sut = CreateSut();

        _ = sut.TriggerManualRunAsync(CancellationToken.None);
        await Task.Delay(100);
        var result2 = await sut.TriggerManualRunAsync(CancellationToken.None);

        result2.Accepted.Should().BeFalse();
        result2.Reason.Should().Contain("already");
    }

    [Fact]
    public async Task SingleInstanceLock_AllowsSequentialRuns()
    {
        _stateStore.GetAllTableStatesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Domain.Entities.TableState>());

        _downstreamClient.CreateBatchAsync(
                Arg.Any<BatchType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("batch-1", "lease-abc"));

        var sut = CreateSut();

        var result1 = await sut.TriggerManualRunAsync(CancellationToken.None);
        var result2 = await sut.TriggerManualRunAsync(CancellationToken.None);

        result1.Accepted.Should().BeTrue();
        result2.Accepted.Should().BeTrue();
    }
}
