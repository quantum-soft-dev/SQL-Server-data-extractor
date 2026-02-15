using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;
using FluentAssertions;

namespace CdcExtractor.Domain.Tests.Entities;

public class BatchRunTests
{
    [Fact]
    public void Create_InitialStatusIsRunning()
    {
        // Act
        var batch = BatchRun.Create(BatchType.Snapshot, BatchTrigger.Scheduled);

        // Assert
        batch.Status.Should().Be(BatchStatus.Running);
    }

    [Fact]
    public void Create_SetsStartedAt()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var batch = BatchRun.Create(BatchType.Delta, BatchTrigger.Manual);

        // Assert
        batch.StartedAt.Should().BeOnOrAfter(before);
        batch.StartedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AddDataset_AddsToCollection()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Snapshot, BatchTrigger.Scheduled);
        var dataset = DatasetRun.Create(new TableIdentifier("dbo", "Orders"), null, null);

        // Act
        batch.AddDataset(dataset);

        // Assert
        batch.Datasets.Should().HaveCount(1);
        batch.Datasets[0].Should().Be(dataset);
    }

    [Fact]
    public void AddDataset_MultipleDatasets_AllPresent()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Delta, BatchTrigger.Manual);
        var ds1 = DatasetRun.Create(new TableIdentifier("dbo", "Orders"), null, null);
        var ds2 = DatasetRun.Create(new TableIdentifier("dbo", "Customers"), null, null);

        // Act
        batch.AddDataset(ds1);
        batch.AddDataset(ds2);

        // Assert
        batch.Datasets.Should().HaveCount(2);
    }

    [Fact]
    public void Finish_Succeeded_SetsStatusAndFinishedAt()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Snapshot, BatchTrigger.Scheduled);

        // Act
        batch.Finish(BatchStatus.Succeeded);

        // Assert
        batch.Status.Should().Be(BatchStatus.Succeeded);
        batch.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public void Finish_Failed_SetsStatus()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Delta, BatchTrigger.Manual);

        // Act
        batch.Finish(BatchStatus.Failed);

        // Assert
        batch.Status.Should().Be(BatchStatus.Failed);
        batch.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public void Finish_Aborted_SetsStatus()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Snapshot, BatchTrigger.Scheduled);

        // Act
        batch.Finish(BatchStatus.Aborted);

        // Assert
        batch.Status.Should().Be(BatchStatus.Aborted);
    }

    [Fact]
    public void Finish_SetsFinishedAtAfterStartedAt()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Delta, BatchTrigger.Manual);

        // Act
        batch.Finish(BatchStatus.Succeeded);

        // Assert
        batch.FinishedAt.Should().BeOnOrAfter(batch.StartedAt);
    }

    [Fact]
    public void AddDataset_AfterFinish_Throws()
    {
        // Arrange
        var batch = BatchRun.Create(BatchType.Snapshot, BatchTrigger.Scheduled);
        batch.Finish(BatchStatus.Succeeded);

        // Act
        var act = () => batch.AddDataset(
            DatasetRun.Create(new TableIdentifier("dbo", "Orders"), null, null));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_SetsTypeAndTrigger()
    {
        // Act
        var batch = BatchRun.Create(BatchType.Delta, BatchTrigger.Manual);

        // Assert
        batch.Type.Should().Be(BatchType.Delta);
        batch.Trigger.Should().Be(BatchTrigger.Manual);
    }

    [Fact]
    public void Create_GeneratesNonDefaultBatchId()
    {
        // Act
        var batch = BatchRun.Create(BatchType.Snapshot, BatchTrigger.Scheduled);

        // Assert
        batch.Id.Should().NotBe(default(BatchId));
    }
}
