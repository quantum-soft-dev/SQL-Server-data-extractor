using CdcExtractor.Domain.ValueObjects;
using FluentAssertions;

namespace CdcExtractor.Domain.Tests.ValueObjects;

public class LsnTests
{
    [Fact]
    public void Parse_HexString_ReturnsCorrectBytes()
    {
        // Arrange
        var hex = "0x00000027000001E80003";

        // Act
        var lsn = Lsn.Parse(hex);

        // Assert
        lsn.Value.Should().HaveCount(10);
        lsn.Value.Should().BeEquivalentTo(
            new byte[] { 0x00, 0x00, 0x00, 0x27, 0x00, 0x00, 0x01, 0xE8, 0x00, 0x03 });
    }

    [Theory]
    [InlineData("0x00000027000001E80003")]
    [InlineData("00000027000001E80003")]
    public void Parse_WithAndWithoutPrefix_ReturnsSameResult(string hex)
    {
        // Act
        var lsn = Lsn.Parse(hex);

        // Assert
        lsn.Value.Should().BeEquivalentTo(
            new byte[] { 0x00, 0x00, 0x00, 0x27, 0x00, 0x00, 0x01, 0xE8, 0x00, 0x03 });
    }

    [Fact]
    public void From_ByteArray_ReturnsCorrectValue()
    {
        // Arrange
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x27, 0x00, 0x00, 0x01, 0xE8, 0x00, 0x03 };

        // Act
        var lsn = Lsn.From(bytes);

        // Assert
        lsn.Value.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public void Comparison_LsnA_LessThan_LsnB()
    {
        // Arrange
        var lsnA = Lsn.Parse("0x00000027000001E80003");
        var lsnB = Lsn.Parse("0x00000027000001E80004");

        // Act & Assert
        lsnA.CompareTo(lsnB).Should().BeNegative();
        lsnB.CompareTo(lsnA).Should().BePositive();
    }

    [Fact]
    public void Comparison_EqualLsns_CompareToReturnsZero()
    {
        // Arrange
        var lsnA = Lsn.Parse("0x00000027000001E80003");
        var lsnB = Lsn.Parse("0x00000027000001E80003");

        // Act & Assert
        lsnA.CompareTo(lsnB).Should().Be(0);
    }

    [Fact]
    public void Empty_IsAllZeros()
    {
        // Act
        var empty = Lsn.Empty;

        // Assert
        empty.Value.Should().HaveCount(10);
        empty.Value.Should().AllBeEquivalentTo((byte)0);
    }

    [Fact]
    public void ToString_ReturnsHexFormatWithPrefix()
    {
        // Arrange
        var lsn = Lsn.Parse("0x00000027000001E80003");

        // Act
        var result = lsn.ToString();

        // Assert
        result.Should().Be("0x00000027000001E80003");
    }

    [Fact]
    public void Equality_SameBytes_AreEqual()
    {
        // Arrange
        var lsnA = Lsn.Parse("0x00000027000001E80003");
        var lsnB = Lsn.From(new byte[] { 0x00, 0x00, 0x00, 0x27, 0x00, 0x00, 0x01, 0xE8, 0x00, 0x03 });

        // Act & Assert
        lsnA.Equals(lsnB).Should().BeTrue();
        (lsnA == lsnB).Should().BeTrue();
        (lsnA != lsnB).Should().BeFalse();
    }

    [Fact]
    public void Equality_DifferentBytes_AreNotEqual()
    {
        // Arrange
        var lsnA = Lsn.Parse("0x00000027000001E80003");
        var lsnB = Lsn.Parse("0x00000027000001E80004");

        // Act & Assert
        lsnA.Equals(lsnB).Should().BeFalse();
        (lsnA == lsnB).Should().BeFalse();
        (lsnA != lsnB).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameBytes_SameHashCode()
    {
        // Arrange
        var lsnA = Lsn.Parse("0x00000027000001E80003");
        var lsnB = Lsn.Parse("0x00000027000001E80003");

        // Act & Assert
        lsnA.GetHashCode().Should().Be(lsnB.GetHashCode());
    }

    [Fact]
    public void IComparable_SortsCorrectly()
    {
        // Arrange
        var lsn1 = Lsn.Parse("0x00000027000001E80005");
        var lsn2 = Lsn.Parse("0x00000027000001E80001");
        var lsn3 = Lsn.Parse("0x00000027000001E80003");
        var list = new List<Lsn> { lsn1, lsn2, lsn3 };

        // Act
        list.Sort();

        // Assert
        list[0].Should().Be(lsn2);
        list[1].Should().Be(lsn3);
        list[2].Should().Be(lsn1);
    }
}
