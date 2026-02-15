using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Models;
using FluentAssertions;

namespace CdcExtractor.Application.Tests;

public class ChunkingServiceTests
{
    private static readonly IReadOnlyList<string> ColumnNames = ["Id", "Name", "Amount"];
    private readonly ChunkingService _sut;

    public ChunkingServiceTests()
    {
        var config = new ExtractionConfig { MaxBytesPerChunk = 1024 };
        _sut = new ChunkingService(config);
    }

    [Fact]
    public async Task ChunkSnapshotRows_EmptyStream_ReturnsNoChunks()
    {
        // Arrange
        var rows = AsyncEnumerable.Empty<DataRow>();

        // Act
        var chunks = await _sut.ChunkSnapshotRowsAsync(ColumnNames, rows);

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkSnapshotRows_SmallDataSet_ReturnsSingleChunk()
    {
        // Arrange
        var rows = CreateRows(5);

        // Act
        var chunks = await _sut.ChunkSnapshotRowsAsync(ColumnNames, rows);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].RowCount.Should().Be(5);
    }

    [Fact]
    public async Task ChunkSnapshotRows_ChunkContainsGzipCompressedData()
    {
        // Arrange
        var rows = CreateRows(3);

        // Act
        var chunks = await _sut.ChunkSnapshotRowsAsync(ColumnNames, rows);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].CompressedSize.Should().BeGreaterThan(0);
        // Gzip magic number: first two bytes should be 0x1F 0x8B
        chunks[0].Data.Position = 0;
        var firstByte = chunks[0].Data.ReadByte();
        var secondByte = chunks[0].Data.ReadByte();
        firstByte.Should().Be(0x1F);
        secondByte.Should().Be(0x8B);
    }

    [Fact]
    public async Task ChunkSnapshotRows_LargeDataSet_SplitsIntoMultipleChunks()
    {
        // Arrange — create enough rows to exceed the 1024 byte MaxBytesPerChunk limit
        var rows = CreateRows(500);

        // Act
        var chunks = await _sut.ChunkSnapshotRowsAsync(ColumnNames, rows);

        // Assert
        chunks.Should().HaveCountGreaterThan(1);
        chunks.Select(c => c.ChunkNumber).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ChunkSnapshotRows_EachChunkHasHeaderRow()
    {
        // Arrange — create enough data to produce multiple chunks
        var rows = CreateRows(500);

        // Act
        var chunks = await _sut.ChunkSnapshotRowsAsync(ColumnNames, rows);

        // Assert — decompress each chunk and verify first line is the CSV header
        foreach (var chunk in chunks)
        {
            chunk.Data.Position = 0;
            using var gzip = new System.IO.Compression.GZipStream(
                chunk.Data, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);
            using var reader = new StreamReader(gzip);
            var headerLine = await reader.ReadLineAsync();
            headerLine.Should().NotBeNullOrWhiteSpace();
            headerLine.Should().Contain("Id");
            headerLine.Should().Contain("Name");
            headerLine.Should().Contain("Amount");
        }
    }

    [Fact]
    public async Task ChunkSnapshotRows_ChunkNumbersStartAtOne()
    {
        // Arrange
        var rows = CreateRows(3);

        // Act
        var chunks = await _sut.ChunkSnapshotRowsAsync(ColumnNames, rows);

        // Assert
        chunks[0].ChunkNumber.Should().Be(1);
    }

    private static async IAsyncEnumerable<DataRow> CreateRows(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new DataRow(new Dictionary<string, object?>
            {
                ["Id"] = i,
                ["Name"] = $"Item-{i:D4}-padding-to-increase-size",
                ["Amount"] = i * 10.5m
            });
        }

        await Task.CompletedTask;
    }
}
