using CdcExtractor.App.ViewModels;
using CdcExtractor.Contracts.Ipc;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CdcExtractor.App.Tests.ViewModels;

public class DashboardViewModelTests
{
    private readonly IExtractorService _service = Substitute.For<IExtractorService>();

    private DashboardViewModel CreateSut() => new(_service);

    private static readonly DateTimeOffset DefaultBatchStart = DateTimeOffset.UtcNow.AddMinutes(-5);
    private static readonly DateTimeOffset DefaultNextRun = DateTimeOffset.UtcNow.AddMinutes(10);

    private static ServiceStatusDto MakeStatus(
        bool isRunning = true,
        string? currentBatchId = "batch-42",
        string? currentBatchType = "Delta",
        string? currentBatchTrigger = "Scheduled",
        DateTimeOffset? currentBatchStartedAt = default,
        DateTimeOffset? nextScheduledRun = default,
        string serviceUptime = "01:23:45",
        int servicePid = 9876,
        bool useDefaults = true) =>
        new(
            isRunning,
            currentBatchId,
            currentBatchType,
            currentBatchTrigger,
            currentBatchStartedAt ?? (useDefaults ? DefaultBatchStart : null),
            nextScheduledRun ?? (useDefaults ? DefaultNextRun : null),
            serviceUptime,
            servicePid);

    private static RecentBatchesDto MakeBatches(int count = 3)
    {
        var batches = Enumerable.Range(1, count).Select(i =>
            new BatchSummaryDto(
                BatchId: $"batch-{i}",
                Type: i % 2 == 0 ? "Delta" : "Snapshot",
                Trigger: "Scheduled",
                Status: "Completed",
                StartedAt: DateTimeOffset.UtcNow.AddHours(-i),
                FinishedAt: DateTimeOffset.UtcNow.AddHours(-i).AddMinutes(2),
                TablesTotal: 5,
                TablesSucceeded: 5,
                TotalRows: 1000 * i)).ToList();

        return new RecentBatchesDto(batches);
    }

    // ---------------------------------------------------------------
    // 1. LoadStatusAsync populates status properties from service DTO
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadStatusAsync_PopulatesStatusPropertiesFromService()
    {
        // Arrange
        var status = MakeStatus(
            isRunning: true,
            serviceUptime: "02:30:00",
            servicePid: 1234);
        _service.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(status);

        var sut = CreateSut();

        // Act
        await sut.LoadStatusCommand.ExecuteAsync(null);

        // Assert
        sut.IsServiceRunning.Should().BeTrue();
        sut.ServiceUptime.Should().Be("02:30:00");
        sut.ServicePid.Should().Be(1234);
        sut.CurrentBatchId.Should().Be("batch-42");
    }

    // ---------------------------------------------------------------
    // 2. LoadStatusAsync when service is idle shows no current batch
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadStatusAsync_WhenServiceIdle_ShowsNullCurrentBatch()
    {
        // Arrange
        var status = MakeStatus(
            isRunning: false,
            currentBatchId: null,
            currentBatchType: null,
            currentBatchTrigger: null,
            serviceUptime: "00:10:00",
            servicePid: 5555,
            useDefaults: false);
        _service.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(status);

        var sut = CreateSut();

        // Act
        await sut.LoadStatusCommand.ExecuteAsync(null);

        // Assert
        sut.IsServiceRunning.Should().BeFalse();
        sut.CurrentBatchId.Should().BeNull();
        sut.NextScheduledRun.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // 3. LoadRecentBatchesAsync populates RecentBatches collection
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadRecentBatchesAsync_PopulatesRecentBatchesCollection()
    {
        // Arrange
        var batches = MakeBatches(3);
        _service.GetRecentBatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(batches);

        var sut = CreateSut();

        // Act
        await sut.LoadRecentBatchesCommand.ExecuteAsync(null);

        // Assert
        sut.RecentBatches.Should().HaveCount(3);
        sut.RecentBatches[0].BatchId.Should().Be("batch-1");
        sut.RecentBatches[1].BatchId.Should().Be("batch-2");
        sut.RecentBatches[2].BatchId.Should().Be("batch-3");
    }

    // ---------------------------------------------------------------
    // 4. LoadRecentBatchesAsync passes limit to service
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadRecentBatchesAsync_PassesDefaultLimitToService()
    {
        // Arrange
        _service.GetRecentBatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new RecentBatchesDto([]));

        var sut = CreateSut();

        // Act
        await sut.LoadRecentBatchesCommand.ExecuteAsync(null);

        // Assert — default limit should be 10
        await _service.Received(1).GetRecentBatchesAsync(10, Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------
    // 5. RefreshCommand triggers both status and batches reload
    // ---------------------------------------------------------------
    [Fact]
    public async Task RefreshCommand_TriggersStatusAndBatchesReload()
    {
        // Arrange
        _service.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(MakeStatus());
        _service.GetRecentBatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MakeBatches(1));

        var sut = CreateSut();

        // Act
        await sut.RefreshCommand.ExecuteAsync(null);

        // Assert — both endpoints should have been called
        await _service.Received().GetStatusAsync(Arg.Any<CancellationToken>());
        await _service.Received().GetRecentBatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------
    // 6. LastBatchDuration computed from most recent finished batch
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadRecentBatches_ComputesLastBatchDuration()
    {
        // Arrange
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var finishedAt = startedAt.AddMinutes(3).AddSeconds(45);
        var batches = new RecentBatchesDto(
        [
            new BatchSummaryDto(
                "batch-1", "Delta", "Scheduled", "Completed",
                startedAt, finishedAt, 4, 4, 5000)
        ]);
        _service.GetRecentBatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(batches);

        var sut = CreateSut();

        // Act
        await sut.LoadRecentBatchesCommand.ExecuteAsync(null);

        // Assert — duration should be 3m 45s
        sut.LastBatchDuration.Should().Be(TimeSpan.FromMinutes(3).Add(TimeSpan.FromSeconds(45)));
    }

    // ---------------------------------------------------------------
    // 7. CdcLag reflects time since last batch started
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadStatus_ComputesCdcLagFromCurrentBatchStartedAt()
    {
        // Arrange
        var batchStart = DateTimeOffset.UtcNow.AddMinutes(-7);
        var status = MakeStatus(currentBatchStartedAt: batchStart);
        _service.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(status);

        var sut = CreateSut();

        // Act
        await sut.LoadStatusCommand.ExecuteAsync(null);

        // Assert — CdcLag should be approximately 7 minutes
        sut.CdcLag.Should().BeCloseTo(TimeSpan.FromMinutes(7), TimeSpan.FromSeconds(5));
    }

    // ---------------------------------------------------------------
    // 8. LoadStatusAsync when service call fails sets error state
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoadStatusAsync_WhenServiceThrows_SetsErrorMessage()
    {
        // Arrange
        _service.GetStatusAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Pipe broken"));

        var sut = CreateSut();

        // Act
        await sut.LoadStatusCommand.ExecuteAsync(null);

        // Assert
        sut.ErrorMessage.Should().Contain("Pipe broken");
        sut.IsServiceRunning.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // 9. RunNowCommand — accepted when idle
    // ---------------------------------------------------------------
    [Fact]
    public async Task RunNowAsync_WhenAccepted_SetsRunNowMessageWithBatchId()
    {
        // Arrange
        _service.TriggerRunAsync(Arg.Any<CancellationToken>())
            .Returns(new TriggerRunResultDto(true, "batch-new", null));
        _service.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(MakeStatus(currentBatchId: "batch-new"));

        var sut = CreateSut();

        // Act
        await sut.RunNowCommand.ExecuteAsync(null);

        // Assert
        sut.RunNowMessage.Should().Contain("batch-new");
    }

    // ---------------------------------------------------------------
    // 10. RunNowCommand — rejected when batch running
    // ---------------------------------------------------------------
    [Fact]
    public async Task RunNowAsync_WhenRejected_SetsReasonMessage()
    {
        // Arrange
        _service.TriggerRunAsync(Arg.Any<CancellationToken>())
            .Returns(new TriggerRunResultDto(false, null, "A batch is already running"));

        var sut = CreateSut();

        // Act
        await sut.RunNowCommand.ExecuteAsync(null);

        // Assert
        sut.RunNowMessage.Should().Contain("already running");
    }

    // ---------------------------------------------------------------
    // 11. RunNowCommand — handles exception
    // ---------------------------------------------------------------
    [Fact]
    public async Task RunNowAsync_WhenServiceThrows_SetsFailureMessage()
    {
        // Arrange
        _service.TriggerRunAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection lost"));

        var sut = CreateSut();

        // Act
        await sut.RunNowCommand.ExecuteAsync(null);

        // Assert
        sut.RunNowMessage.Should().Contain("Connection lost");
    }

    // ---------------------------------------------------------------
    // 12. CanRunNow — reflects IsBatchActive state
    // ---------------------------------------------------------------
    [Fact]
    public async Task CanRunNow_IsFalse_WhenBatchIsActive()
    {
        // Arrange
        var status = MakeStatus(isRunning: true, currentBatchId: "batch-42");
        _service.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(status);

        var sut = CreateSut();

        // Act
        await sut.LoadStatusCommand.ExecuteAsync(null);

        // Assert
        sut.IsBatchActive.Should().BeTrue();
        sut.CanRunNow.Should().BeFalse();
    }

    [Fact]
    public async Task CanRunNow_IsTrue_WhenIdle()
    {
        // Arrange
        var status = MakeStatus(
            isRunning: false,
            currentBatchId: null,
            currentBatchType: null,
            currentBatchTrigger: null,
            useDefaults: false);
        _service.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(status);

        var sut = CreateSut();

        // Act
        await sut.LoadStatusCommand.ExecuteAsync(null);

        // Assert
        sut.IsBatchActive.Should().BeFalse();
        sut.CanRunNow.Should().BeTrue();
    }
}
