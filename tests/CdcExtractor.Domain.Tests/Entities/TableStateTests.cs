using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;
using FluentAssertions;

namespace CdcExtractor.Domain.Tests.Entities;

public class TableStateTests
{
    private static TableState CreatePendingState() =>
        new(
            new TableIdentifier("dbo", "Orders"),
            ExtractionMode.Cdc);

    [Fact]
    public void InitialState_BootstrapStatusIsPending()
    {
        // Arrange & Act
        var state = CreatePendingState();

        // Assert
        state.BootstrapStatus.Should().Be(BootstrapStatus.Pending);
    }

    [Fact]
    public void MarkComplete_FromPending_SetsStatusToComplete()
    {
        // Arrange
        var state = CreatePendingState();
        var lsn = Lsn.Parse("0x00000027000001E80003");
        var hash = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");
        var syncTime = DateTimeOffset.UtcNow;

        // Act
        state.MarkComplete(lsn, hash, syncTime);

        // Assert
        state.BootstrapStatus.Should().Be(BootstrapStatus.Complete);
        state.LastSyncTime.Should().Be(syncTime);
    }

    [Fact]
    public void MarkComplete_UpdatesLastProcessedLsn()
    {
        // Arrange
        var state = CreatePendingState();
        var lsn = Lsn.Parse("0x00000027000001E80003");
        var hash = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");
        var syncTime = DateTimeOffset.UtcNow;

        // Act
        state.MarkComplete(lsn, hash, syncTime);

        // Assert
        state.LastProcessedLsn.Should().Be(lsn);
    }

    [Fact]
    public void MarkComplete_UpdatesSchemaHash()
    {
        // Arrange
        var state = CreatePendingState();
        var lsn = Lsn.Parse("0x00000027000001E80003");
        var hash = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");
        var syncTime = DateTimeOffset.UtcNow;

        // Act
        state.MarkComplete(lsn, hash, syncTime);

        // Assert
        state.SchemaHash.Should().Be(hash);
    }

    [Fact]
    public void MarkReBootstrap_FromComplete_SetsStatusToReBootstrap()
    {
        // Arrange
        var state = CreatePendingState();
        var lsn = Lsn.Parse("0x00000027000001E80003");
        var hash = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");
        state.MarkComplete(lsn, hash, DateTimeOffset.UtcNow);

        // Act
        state.MarkReBootstrap("Schema drift detected");

        // Assert
        state.BootstrapStatus.Should().Be(BootstrapStatus.ReBootstrap);
    }

    [Fact]
    public void MarkComplete_FromReBootstrap_ResetsToComplete()
    {
        // Arrange
        var state = CreatePendingState();
        var lsn1 = Lsn.Parse("0x00000027000001E80003");
        var hash1 = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");
        state.MarkComplete(lsn1, hash1, DateTimeOffset.UtcNow);
        state.MarkReBootstrap("Schema drift detected");

        var lsn2 = Lsn.Parse("0x00000027000001E80005");
        var hash2 = new SchemaHash("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
        var syncTime = DateTimeOffset.UtcNow;

        // Act
        state.MarkComplete(lsn2, hash2, syncTime);

        // Assert
        state.BootstrapStatus.Should().Be(BootstrapStatus.Complete);
        state.LastProcessedLsn.Should().Be(lsn2);
    }

    [Fact]
    public void MarkReBootstrap_FromPending_Throws()
    {
        // Arrange
        var state = CreatePendingState();

        // Act
        var act = () => state.MarkReBootstrap("Invalid transition");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ErrorMessage_CanBeSetAndCleared()
    {
        // Arrange
        var state = CreatePendingState();

        // Act - Set error
        state.SetError("Something went wrong");

        // Assert
        state.ErrorMessage.Should().Be("Something went wrong");

        // Act - Clear error
        state.ClearError();

        // Assert
        state.ErrorMessage.Should().BeNull();
    }
}
