namespace CdcExtractor.Domain.ValueObjects;

/// <summary>
/// Immutable value object representing a fully qualified SQL Server table (schema + name).
/// Equality is case-insensitive.
/// </summary>
public sealed class TableIdentifier : IEquatable<TableIdentifier>
{
    public string Schema { get; }
    public string Name { get; }

    public string FullName => $"{Schema}.{Name}";
    public string QuotedFullName => $"[{Schema}].[{Name}]";

    public TableIdentifier(string schema, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Schema = schema;
        Name = name;
    }

    public bool Equals(TableIdentifier? other)
    {
        if (other is null) return false;

        return string.Equals(Schema, other.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is TableIdentifier other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            Schema.ToUpperInvariant(),
            Name.ToUpperInvariant());

    public override string ToString() => FullName;

    public static bool operator ==(TableIdentifier? left, TableIdentifier? right) => Equals(left, right);
    public static bool operator !=(TableIdentifier? left, TableIdentifier? right) => !Equals(left, right);
}
