using CdcExtractor.Domain.ValueObjects;
using FluentAssertions;

namespace CdcExtractor.Domain.Tests.ValueObjects;

public class TableIdentifierTests
{
    [Fact]
    public void FullName_ReturnsSchemaAndTableDotSeparated()
    {
        // Arrange
        var table = new TableIdentifier("dbo", "Orders");

        // Act
        var result = table.FullName;

        // Assert
        result.Should().Be("dbo.Orders");
    }

    [Fact]
    public void QuotedFullName_ReturnsBracketedSchemaAndTable()
    {
        // Arrange
        var table = new TableIdentifier("dbo", "Orders");

        // Act
        var result = table.QuotedFullName;

        // Assert
        result.Should().Be("[dbo].[Orders]");
    }

    [Fact]
    public void Equality_SameSchemaAndName_AreEqual()
    {
        // Arrange
        var a = new TableIdentifier("dbo", "Orders");
        var b = new TableIdentifier("dbo", "Orders");

        // Act & Assert
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void Equality_DifferentSchema_AreNotEqual()
    {
        // Arrange
        var a = new TableIdentifier("dbo", "Orders");
        var b = new TableIdentifier("sales", "Orders");

        // Act & Assert
        a.Equals(b).Should().BeFalse();
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void Equality_DifferentName_AreNotEqual()
    {
        // Arrange
        var a = new TableIdentifier("dbo", "Orders");
        var b = new TableIdentifier("dbo", "Customers");

        // Act & Assert
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void CaseHandling_PreservesOriginalCase()
    {
        // Arrange
        var table = new TableIdentifier("DBO", "MyTable");

        // Act & Assert
        table.Schema.Should().Be("DBO");
        table.Name.Should().Be("MyTable");
    }

    [Fact]
    public void Constructor_NullSchema_Throws()
    {
        // Act
        var act = () => new TableIdentifier(null!, "Orders");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullName_Throws()
    {
        // Act
        var act = () => new TableIdentifier("dbo", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetHashCode_SameSchemaAndName_SameHashCode()
    {
        // Arrange
        var a = new TableIdentifier("dbo", "Orders");
        var b = new TableIdentifier("dbo", "Orders");

        // Act & Assert
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Properties_SchemaAndName_ReturnCorrectValues()
    {
        // Arrange
        var table = new TableIdentifier("sales", "Invoices");

        // Act & Assert
        table.Schema.Should().Be("sales");
        table.Name.Should().Be("Invoices");
    }
}
