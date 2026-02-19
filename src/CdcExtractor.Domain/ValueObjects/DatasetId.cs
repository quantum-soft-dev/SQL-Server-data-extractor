namespace CdcExtractor.Domain.ValueObjects;

/// <summary>
/// Immutable value object wrapping a <see cref="Guid"/> that identifies a dataset.
/// </summary>
public sealed class DatasetId : IEquatable<DatasetId>
{
    public Guid Value { get; }

    public DatasetId(Guid value)
    {
        Value = value;
    }

    public static DatasetId New() => new(Guid.NewGuid());

    public static DatasetId Parse(string s)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(s);
        return new DatasetId(Guid.Parse(s));
    }

    public bool Equals(DatasetId? other)
    {
        if (other is null) return false;
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj) => obj is DatasetId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();

    public static bool operator ==(DatasetId? left, DatasetId? right) => Equals(left, right);
    public static bool operator !=(DatasetId? left, DatasetId? right) => !Equals(left, right);
}
