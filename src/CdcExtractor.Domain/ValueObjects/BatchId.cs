namespace CdcExtractor.Domain.ValueObjects;

/// <summary>
/// Immutable value object wrapping a <see cref="Guid"/> that identifies a batch.
/// </summary>
public sealed class BatchId : IEquatable<BatchId>
{
    public Guid Value { get; }

    public BatchId(Guid value)
    {
        Value = value;
    }

    public static BatchId New() => new(Guid.NewGuid());

    public static BatchId Parse(string s)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(s);
        return new BatchId(Guid.Parse(s));
    }

    public bool Equals(BatchId? other)
    {
        if (other is null) return false;
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj) => obj is BatchId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();

    public static bool operator ==(BatchId? left, BatchId? right) => Equals(left, right);
    public static bool operator !=(BatchId? left, BatchId? right) => !Equals(left, right);
}
