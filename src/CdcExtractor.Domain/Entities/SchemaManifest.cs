using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Entities;

/// <summary>
/// Immutable snapshot of a table's schema at a point in time.
/// </summary>
public sealed record SchemaManifest(
    TableIdentifier Table,
    DateTimeOffset CapturedAt,
    SchemaHash Hash,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<IReadOnlyList<string>> UniqueKeys,
    string? WatermarkColumn);

/// <summary>
/// Describes a single column within a <see cref="SchemaManifest"/>.
/// </summary>
public sealed record ColumnInfo(
    string Name,
    string SqlType,
    bool IsNullable,
    int? MaxLength,
    byte? Precision,
    byte? Scale,
    int OrdinalPosition);
