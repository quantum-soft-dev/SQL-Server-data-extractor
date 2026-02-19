using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.Models;
using CdcExtractor.Domain.ValueObjects;
using CdcExtractor.Application.Models;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CdcExtractor.Application.Tests;

public class SnapshotServiceTests
{
    private readonly ICdcReader _cdcReader;
    private readonly IDownstreamClient _downstreamClient;
    private readonly ChunkingService _chunkingService;
    private readonly IStateStore _stateStore;
    private readonly SnapshotService _sut;

    private static readonly TableIdentifier TestTable = new("dbo", "Orders");
    private static readonly Lsn TestLsn = Lsn.Parse("0x00000027000001E80003");

    private static readonly SchemaManifest TestManifest = new(
        TestTable,
        DateTimeOffset.UtcNow,
        new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1"),
        new List<ColumnInfo>
        {
            new("Id", "int", false, null, 10, 0, 1),
            new("Name", "nvarchar", true, 100, null, null, 2)
        },
        new List<string> { "Id" },
        new List<IReadOnlyList<string>>(),
        null);

    public SnapshotServiceTests()
    {
        _cdcReader = Substitute.For<ICdcReader>();
        _downstreamClient = Substitute.For<IDownstreamClient>();
        _stateStore = Substitute.For<IStateStore>();

        var config = new ExtractionConfig { MaxBytesPerChunk = 10_485_760 };
        _chunkingService = new ChunkingService(config);

        _sut = new SnapshotService(_cdcReader, _downstreamClient, _chunkingService, _stateStore);
    }

    [Fact]
    public async Task ExtractSnapshot_CapturesMaxLsnBeforeReadingTable()
    {
        // Arrange — R-001 algorithm: get max LSN before starting table read
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(TestLsn);
        _cdcReader.ReadFullTableAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<DataRow>());
        _downstreamClient.CreateDatasetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("dataset-1");

        // Act
        await _sut.ExtractSnapshotAsync(TestTable, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — GetMaxLsn must be called before ReadFullTable
        Received.InOrder(() =>
        {
            _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>());
            _cdcReader.ReadFullTableAsync(TestTable, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ExtractSnapshot_UploadsChunksToDownstream()
    {
        // Arrange
        var rows = new List<DataRow>
        {
            new(new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Order1" }),
            new(new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "Order2" })
        };
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(TestLsn);
        _cdcReader.ReadFullTableAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(rows.ToAsyncEnumerable());
        _downstreamClient.CreateDatasetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("dataset-1");
        _stateStore.GetTableStateAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns((TableState?)null);

        // Act
        await _sut.ExtractSnapshotAsync(TestTable, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert
        await _downstreamClient.Received().UploadChunkAsync(
            "dataset-1",
            "lease-1",
            Arg.Any<int>(),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractSnapshot_CommitsDatasetAfterUpload()
    {
        // Arrange
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(TestLsn);
        _cdcReader.ReadFullTableAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<DataRow>());
        _downstreamClient.CreateDatasetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("dataset-1");
        _stateStore.GetTableStateAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns((TableState?)null);

        // Act
        await _sut.ExtractSnapshotAsync(TestTable, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert
        await _downstreamClient.Received().CommitDatasetAsync(
            "dataset-1", "lease-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractSnapshot_ReturnsDatasetRun()
    {
        // Arrange
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(TestLsn);
        _cdcReader.ReadFullTableAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<DataRow>());
        _downstreamClient.CreateDatasetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("dataset-1");
        _stateStore.GetTableStateAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns((TableState?)null);

        // Act
        var result = await _sut.ExtractSnapshotAsync(TestTable, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — returned DatasetRun should have the table and be committed
        result.TableId.Should().Be(TestTable);
    }

    [Fact]
    public async Task ExtractSnapshot_UploadFailure_AbortsDatasetAndThrows()
    {
        // Arrange
        var rows = new List<DataRow>
        {
            new(new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Order1" })
        };
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(TestLsn);
        _cdcReader.ReadFullTableAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(rows.ToAsyncEnumerable());
        _downstreamClient.CreateDatasetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("dataset-1");
        _downstreamClient.UploadChunkAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Upload failed"));

        // Act
        var act = () => _sut.ExtractSnapshotAsync(TestTable, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _downstreamClient.Received().AbortDatasetAsync(
            "dataset-1", "lease-1", Arg.Any<CancellationToken>());
    }
}
