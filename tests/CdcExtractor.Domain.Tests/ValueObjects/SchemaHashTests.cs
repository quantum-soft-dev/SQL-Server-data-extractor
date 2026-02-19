using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.ValueObjects;
using FluentAssertions;

namespace CdcExtractor.Domain.Tests.ValueObjects;

public class SchemaHashTests
{
    private static SchemaManifest CreateManifest(string tableName, string columnType) =>
        new(
            new TableIdentifier("dbo", tableName),
            DateTimeOffset.UnixEpoch,
            new SchemaHash("placeholder"),
            [new ColumnInfo("Id", columnType, false, null, null, null, 1)],
            ["Id"],
            [],
            null);

    [Fact]
    public void Compute_FromSchemaManifest_ProducesSha256HexString()
    {
        var manifest = CreateManifest("Orders", "int");

        var hash = SchemaHash.Compute(manifest);

        hash.Value.Should().NotBeNullOrWhiteSpace();
        hash.Value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_SameManifest_ProducesSameHash()
    {
        var manifest1 = CreateManifest("Orders", "int");
        var manifest2 = CreateManifest("Orders", "int");

        var hash1 = SchemaHash.Compute(manifest1);
        var hash2 = SchemaHash.Compute(manifest2);

        hash1.Value.Should().Be(hash2.Value);
    }

    [Fact]
    public void Compute_DifferentManifests_ProduceDifferentHashes()
    {
        var manifest1 = CreateManifest("Orders", "int");
        var manifest2 = CreateManifest("Orders", "bigint");

        var hash1 = SchemaHash.Compute(manifest1);
        var hash2 = SchemaHash.Compute(manifest2);

        hash1.Value.Should().NotBe(hash2.Value);
    }

    [Fact]
    public void Equality_SameHashValue_AreEqual()
    {
        var hash1 = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");
        var hash2 = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");

        hash1.Equals(hash2).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentHashValue_AreNotEqual()
    {
        var hash1 = new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");
        var hash2 = new SchemaHash("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

        hash1.Equals(hash2).Should().BeFalse();
    }

    [Fact]
    public void Value_ReturnsTheHexDigestString()
    {
        var expected = "abc123def456abc123def456abc123def456abc123def456abc123def456abc1";
        var hash = new SchemaHash(expected);

        hash.Value.Should().Be(expected);
    }
}
