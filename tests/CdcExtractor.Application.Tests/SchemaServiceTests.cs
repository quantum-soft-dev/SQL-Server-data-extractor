using CdcExtractor.Application.Services;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace CdcExtractor.Application.Tests;

public class SchemaServiceTests
{
    private readonly ISchemaInspector _schemaInspector;
    private readonly IDownstreamClient _downstreamClient;
    private readonly SchemaService _sut;

    private static readonly TableIdentifier TestTable = new("dbo", "Orders");

    public SchemaServiceTests()
    {
        _schemaInspector = Substitute.For<ISchemaInspector>();
        _downstreamClient = Substitute.For<IDownstreamClient>();
        _sut = new SchemaService(_schemaInspector, _downstreamClient);
    }

    [Fact]
    public async Task InspectAndUploadSchema_ReturnsManifestWithAllColumns()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("Id", "int", false, null, 10, 0, 1),
            new("Name", "nvarchar", true, 100, null, null, 2),
            new("Amount", "decimal", false, null, 18, 2, 3)
        };
        var hash = SchemaHash.Compute(new { Columns = columns });
        var manifest = new SchemaManifest(
            TestTable,
            DateTimeOffset.UtcNow,
            hash,
            columns,
            new List<string> { "Id" },
            new List<IReadOnlyList<string>>(),
            null);
        _schemaInspector.GetTableMetadataAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(manifest);

        // Act
        var result = await _sut.InspectAndUploadSchemaAsync(TestTable, "batch-1", "lease-1", CancellationToken.None);

        // Assert
        result.Columns.Should().HaveCount(3);
        result.Table.Should().Be(TestTable);
        result.PrimaryKey.Should().Contain("Id");
    }

    [Fact]
    public async Task InspectAndUploadSchema_ComputesHashFromManifest()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("Id", "int", false, null, 10, 0, 1)
        };
        var hash = SchemaHash.Compute(new { Columns = columns });
        var manifest = new SchemaManifest(
            TestTable,
            DateTimeOffset.UtcNow,
            hash,
            columns,
            new List<string> { "Id" },
            new List<IReadOnlyList<string>>(),
            null);
        _schemaInspector.GetTableMetadataAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(manifest);

        // Act
        var result = await _sut.InspectAndUploadSchemaAsync(TestTable, "batch-1", "lease-1", CancellationToken.None);

        // Assert
        result.Hash.Should().NotBeNull();
        result.Hash.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void HasSchemaChanged_SameHash_ReturnsFalse()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("Id", "int", false, null, 10, 0, 1)
        };
        var hash = SchemaHash.Compute(new { Columns = columns });

        // Act
        var changed = _sut.HasSchemaChanged(hash, hash);

        // Assert
        changed.Should().BeFalse();
    }

    [Fact]
    public void HasSchemaChanged_DifferentHash_ReturnsTrue()
    {
        // Arrange
        var previousHash = new SchemaHash("0000000000000000000000000000000000000000000000000000000000000000");
        var columns = new List<ColumnInfo>
        {
            new("Id", "int", false, null, 10, 0, 1),
            new("NewColumn", "nvarchar", true, 50, null, null, 2)
        };
        var currentHash = SchemaHash.Compute(new { Columns = columns });

        // Act
        var changed = _sut.HasSchemaChanged(previousHash, currentHash);

        // Assert
        changed.Should().BeTrue();
    }

    [Fact]
    public async Task InspectAndUploadSchema_CallsDownstreamWithManifestAndHash()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("Id", "int", false, null, 10, 0, 1)
        };
        var hash = SchemaHash.Compute(new { Columns = columns });
        var manifest = new SchemaManifest(
            TestTable,
            DateTimeOffset.UtcNow,
            hash,
            columns,
            new List<string> { "Id" },
            new List<IReadOnlyList<string>>(),
            null);
        _schemaInspector.GetTableMetadataAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(manifest);

        // Act
        await _sut.InspectAndUploadSchemaAsync(TestTable, "batch-1", "lease-1", CancellationToken.None);

        // Assert
        await _downstreamClient.Received(1).UploadSchemaAsync(
            TestTable.FullName,
            hash.Value,
            Arg.Is<SchemaManifest>(m => m.Table == TestTable),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InspectAndUploadSchema_IncludesWatermarkColumn_WhenPresent()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("Id", "int", false, null, 10, 0, 1),
            new("ModifiedAt", "datetime2", false, null, null, null, 2)
        };
        var hash = SchemaHash.Compute(new { Columns = columns });
        var manifest = new SchemaManifest(
            TestTable,
            DateTimeOffset.UtcNow,
            hash,
            columns,
            new List<string> { "Id" },
            new List<IReadOnlyList<string>>(),
            "ModifiedAt");
        _schemaInspector.GetTableMetadataAsync(TestTable, Arg.Any<CancellationToken>())
            .Returns(manifest);

        // Act
        var result = await _sut.InspectAndUploadSchemaAsync(TestTable, "batch-1", "lease-1", CancellationToken.None);

        // Assert
        result.WatermarkColumn.Should().Be("ModifiedAt");
    }

    [Fact]
    public void HasSchemaChanged_NullPreviousHash_ReturnsTrue()
    {
        // Arrange
        var currentHash = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");

        // Act
        var changed = _sut.HasSchemaChanged(null, currentHash);

        // Assert
        changed.Should().BeTrue();
    }
}
