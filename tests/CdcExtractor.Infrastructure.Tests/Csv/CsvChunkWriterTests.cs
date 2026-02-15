using System.IO.Compression;
using CdcExtractor.Domain.Models;
using CdcExtractor.Infrastructure.Csv;
using FluentAssertions;

namespace CdcExtractor.Infrastructure.Tests.Csv;

public class CsvChunkWriterTests
{
    private readonly CsvChunkWriter _sut = new();

    private static string DecompressGzipToString(Stream gzipStream)
    {
        gzipStream.Position = 0;
        using var decompressor = new GZipStream(gzipStream, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressor);
        return reader.ReadToEnd();
    }

    [Fact]
    public void WriteSnapshotChunk_ProducesRfc4180HeaderRow()
    {
        // Arrange
        var columns = new List<string> { "Id", "Name", "Email" };
        var rows = new List<DataRow>
        {
            new(new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Alice", ["Email"] = "a@b.com" })
        };

        // Act
        using var stream = _sut.WriteSnapshotChunk(columns, rows);

        // Assert
        var csv = DecompressGzipToString(stream);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Id,Name,Email");
    }

    [Fact]
    public void WriteSnapshotChunk_OutputIsValidGzip()
    {
        // Arrange
        var columns = new List<string> { "Id" };
        var rows = new List<DataRow>
        {
            new(new Dictionary<string, object?> { ["Id"] = 42 })
        };

        // Act
        using var stream = _sut.WriteSnapshotChunk(columns, rows);

        // Assert
        stream.Position.Should().Be(0);
        var header = new byte[2];
        _ = stream.Read(header, 0, 2);
        header[0].Should().Be(0x1F, "first byte of gzip magic number");
        header[1].Should().Be(0x8B, "second byte of gzip magic number");
    }

    [Fact]
    public void WriteSnapshotChunk_MapsColumnsFromDataRowInOrder()
    {
        // Arrange
        var columns = new List<string> { "ProductId", "Price" };
        var rows = new List<DataRow>
        {
            new(new Dictionary<string, object?> { ["ProductId"] = 101, ["Price"] = 9.99m })
        };

        // Act
        using var stream = _sut.WriteSnapshotChunk(columns, rows);

        // Assert
        var csv = DecompressGzipToString(stream);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[1].Should().Be("101,9.99");
    }

    [Fact]
    public void WriteSnapshotChunk_MultipleRows_AllWrittenCorrectly()
    {
        // Arrange
        var columns = new List<string> { "Id", "Status" };
        var rows = new List<DataRow>
        {
            new(new Dictionary<string, object?> { ["Id"] = 1, ["Status"] = "Active" }),
            new(new Dictionary<string, object?> { ["Id"] = 2, ["Status"] = "Inactive" }),
            new(new Dictionary<string, object?> { ["Id"] = 3, ["Status"] = "Pending" })
        };

        // Act
        using var stream = _sut.WriteSnapshotChunk(columns, rows);

        // Assert
        var csv = DecompressGzipToString(stream);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(4); // header + 3 data rows
        lines[1].Should().Be("1,Active");
        lines[2].Should().Be("2,Inactive");
        lines[3].Should().Be("3,Pending");
    }

    [Fact]
    public void WriteSnapshotChunk_FieldContainingComma_IsQuotedPerRfc4180()
    {
        // Arrange
        var columns = new List<string> { "Name" };
        var rows = new List<DataRow>
        {
            new(new Dictionary<string, object?> { ["Name"] = "Smith, John" })
        };

        // Act
        using var stream = _sut.WriteSnapshotChunk(columns, rows);

        // Assert
        var csv = DecompressGzipToString(stream);
        csv.Should().Contain("\"Smith, John\"");
    }

    [Fact]
    public void WriteSnapshotChunk_NullValue_WrittenAsEmptyField()
    {
        // Arrange
        var columns = new List<string> { "Id", "Notes" };
        var rows = new List<DataRow>
        {
            new(new Dictionary<string, object?> { ["Id"] = 1, ["Notes"] = null })
        };

        // Act
        using var stream = _sut.WriteSnapshotChunk(columns, rows);

        // Assert
        var csv = DecompressGzipToString(stream);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[1].Should().Be("1,");
    }
}
