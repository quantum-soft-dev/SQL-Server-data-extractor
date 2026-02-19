using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdcExtractor.Domain.ValueObjects;

/// <summary>
/// Immutable value object representing a SHA-256 hex digest of a schema manifest.
/// </summary>
public sealed class SchemaHash : IEquatable<SchemaHash>
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>The SHA-256 hex digest (lowercase).</summary>
    public string Value { get; }

    /// <summary>
    /// Constructs a <see cref="SchemaHash"/> from an existing hex digest (for deserialization).
    /// </summary>
    public SchemaHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// Computes a <see cref="SchemaHash"/> from a SchemaManifest entity by serializing it
    /// to canonical JSON and hashing with SHA-256.
    /// </summary>
    /// <remarks>
    /// SchemaManifest will be defined in CdcExtractor.Domain.Entities by another task.
    /// This method accepts <c>object</c> so the Domain project compiles before that entity exists.
    /// Once SchemaManifest is available, the parameter type can be narrowed.
    /// </remarks>
    public static SchemaHash Compute(object manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var json = JsonSerializer.Serialize(manifest, manifest.GetType(), CanonicalJsonOptions);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        var hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return new SchemaHash(hex);
    }

    public bool Equals(SchemaHash? other)
    {
        if (other is null) return false;
        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is SchemaHash other && Equals(other);

    public override int GetHashCode() => Value.ToUpperInvariant().GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;
}
