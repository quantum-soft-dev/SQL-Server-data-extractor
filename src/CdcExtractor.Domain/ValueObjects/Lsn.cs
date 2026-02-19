using System.Globalization;

namespace CdcExtractor.Domain.ValueObjects;

/// <summary>
/// Immutable value object wrapping a SQL Server LSN (binary(10)).
/// </summary>
public sealed class Lsn : IComparable<Lsn>, IEquatable<Lsn>
{
    private readonly byte[] _value;

    /// <summary>Sentinel representing an all-zero LSN.</summary>
    public static Lsn Empty { get; } = new(new byte[10]);

    /// <summary>
    /// Returns a defensive copy of the underlying 10-byte LSN.
    /// </summary>
    public byte[] Value => (byte[])_value.Clone();

    private Lsn(byte[] value)
    {
        _value = value;
    }

    /// <summary>
    /// Creates an <see cref="Lsn"/> from a raw byte array that must be exactly 10 bytes.
    /// </summary>
    public static Lsn From(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (raw.Length != 10)
            throw new ArgumentException("LSN must be exactly 10 bytes.", nameof(raw));

        var copy = new byte[10];
        raw.CopyTo(copy, 0);
        return new Lsn(copy);
    }

    /// <summary>
    /// Parses a hex string like "0x00000027000001E80003" or "00000027000001E80003".
    /// </summary>
    public static Lsn Parse(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);

        var span = hex.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            span = span[2..];

        if (span.Length != 20)
            throw new ArgumentException("Hex string must represent exactly 10 bytes (20 hex characters).", nameof(hex));

        var bytes = new byte[10];
        for (var i = 0; i < 10; i++)
        {
            bytes[i] = byte.Parse(span.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return new Lsn(bytes);
    }

    public int CompareTo(Lsn? other)
    {
        if (other is null) return 1;

        for (var i = 0; i < 10; i++)
        {
            var cmp = _value[i].CompareTo(other._value[i]);
            if (cmp != 0) return cmp;
        }

        return 0;
    }

    public bool Equals(Lsn? other)
    {
        if (other is null) return false;
        return _value.AsSpan().SequenceEqual(other._value);
    }

    public override bool Equals(object? obj) => obj is Lsn other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in _value)
            hash.Add(b);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns the LSN as a hex string with 0x prefix, e.g. "0x00000027000001E80003".
    /// </summary>
    public override string ToString() => $"0x{Convert.ToHexString(_value)}";

    public static bool operator ==(Lsn? left, Lsn? right) => Equals(left, right);
    public static bool operator !=(Lsn? left, Lsn? right) => !Equals(left, right);
}
