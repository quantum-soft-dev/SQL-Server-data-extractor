using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.Models;
using CdcExtractor.Domain.ValueObjects;
using Dapper;

namespace CdcExtractor.Infrastructure.SqlServer;

/// <summary>
/// Inspects SQL Server metadata to discover tables and retrieve schema manifests using Dapper.
/// </summary>
public sealed class SchemaInspector : ISchemaInspector
{
    private readonly SqlConnectionFactory _connectionFactory;

    private const string GetColumnsSql = """
        SELECT c.COLUMN_NAME       AS Name,
               c.DATA_TYPE         AS SqlType,
               c.IS_NULLABLE       AS IsNullableStr,
               c.CHARACTER_MAXIMUM_LENGTH AS MaxLength,
               c.NUMERIC_PRECISION AS Precision,
               c.NUMERIC_SCALE     AS Scale,
               c.ORDINAL_POSITION  AS OrdinalPosition
        FROM INFORMATION_SCHEMA.COLUMNS c
        WHERE c.TABLE_SCHEMA = @Schema
          AND c.TABLE_NAME   = @Name
        ORDER BY c.ORDINAL_POSITION;
        """;

    private const string GetPrimaryKeySql = """
        SELECT col.name
        FROM sys.indexes ix
        JOIN sys.index_columns ic ON ix.object_id = ic.object_id AND ix.index_id = ic.index_id
        JOIN sys.columns col ON ic.object_id = col.object_id AND ic.column_id = col.column_id
        WHERE ix.is_primary_key = 1
          AND ix.object_id = OBJECT_ID(@QuotedFullName)
        ORDER BY ic.key_ordinal;
        """;

    private const string GetUniqueKeysSql = """
        SELECT ix.name AS IndexName, col.name AS ColumnName
        FROM sys.indexes ix
        JOIN sys.index_columns ic ON ix.object_id = ic.object_id AND ix.index_id = ic.index_id
        JOIN sys.columns col ON ic.object_id = col.object_id AND ic.column_id = col.column_id
        WHERE ix.is_unique = 1
          AND ix.is_primary_key = 0
          AND ix.object_id = OBJECT_ID(@QuotedFullName)
        ORDER BY ix.name, ic.key_ordinal;
        """;

    private const string GetAllTablesSql = """
        SELECT s.name  AS SchemaName,
               t.name  AS TableName,
               t.is_tracked_by_cdc AS CdcEnabled,
               CAST(CASE WHEN EXISTS (
                   SELECT 1
                   FROM sys.indexes i
                   WHERE i.object_id = t.object_id
                     AND (i.is_primary_key = 1 OR i.is_unique = 1)
               ) THEN 1 ELSE 0 END AS BIT) AS HasUniqueKey,
               ISNULL(SUM(p.rows), 0)       AS EstimatedRowCount
        FROM sys.tables t
        JOIN sys.schemas s ON t.schema_id = s.schema_id
        LEFT JOIN sys.dm_db_partition_stats p
            ON t.object_id = p.object_id AND p.index_id IN (0, 1)
        WHERE t.is_ms_shipped = 0
        GROUP BY s.name, t.name, t.is_tracked_by_cdc, t.object_id;
        """;

    public SchemaInspector(SqlConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<SchemaManifest> GetTableMetadataAsync(TableIdentifier table, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        // Fetch columns
        var columnRows = await connection.QueryAsync<ColumnRow>(
            new CommandDefinition(GetColumnsSql,
                new { table.Schema, table.Name },
                cancellationToken: ct))
            .ConfigureAwait(false);

        var columns = columnRows
            .Select(r => new ColumnInfo(
                r.Name,
                r.SqlType,
                string.Equals(r.IsNullableStr, "YES", StringComparison.OrdinalIgnoreCase),
                r.MaxLength,
                r.Precision,
                r.Scale,
                r.OrdinalPosition))
            .ToList()
            .AsReadOnly();

        // Fetch primary key columns
        var pkColumns = (await connection.QueryAsync<string>(
            new CommandDefinition(GetPrimaryKeySql,
                new { table.QuotedFullName },
                cancellationToken: ct))
            .ConfigureAwait(false))
            .ToList()
            .AsReadOnly();

        // Fetch unique keys (grouped by index name)
        var uniqueKeyRows = await connection.QueryAsync<UniqueKeyRow>(
            new CommandDefinition(GetUniqueKeysSql,
                new { table.QuotedFullName },
                cancellationToken: ct))
            .ConfigureAwait(false);

        var uniqueKeys = uniqueKeyRows
            .GroupBy(r => r.IndexName)
            .Select(g => (IReadOnlyList<string>)g.Select(r => r.ColumnName).ToList().AsReadOnly())
            .ToList()
            .AsReadOnly();

        // Determine watermark column: first datetime/datetime2/datetimeoffset column, if any
        var watermarkColumn = columns
            .FirstOrDefault(c => c.SqlType is "datetime" or "datetime2" or "datetimeoffset")
            ?.Name;

        var capturedAt = DateTimeOffset.UtcNow;

        // Build a temporary manifest without hash to compute the hash from it
        var tempManifest = new SchemaManifest(
            table,
            capturedAt,
            new SchemaHash("placeholder"),
            columns,
            pkColumns,
            uniqueKeys,
            watermarkColumn);

        var hash = SchemaHash.Compute(tempManifest);

        return new SchemaManifest(
            table,
            capturedAt,
            hash,
            columns,
            pkColumns,
            uniqueKeys,
            watermarkColumn);
    }

    public async Task<IReadOnlyList<TableDiscoveryInfo>> GetAllTablesAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<TableDiscoveryRow>(
            new CommandDefinition(GetAllTablesSql, cancellationToken: ct))
            .ConfigureAwait(false);

        return rows
            .Select(r =>
            {
                var tableId = new TableIdentifier(r.SchemaName, r.TableName);
                var suggestedMode = r.CdcEnabled || r.HasUniqueKey
                    ? ExtractionMode.Cdc
                    : ExtractionMode.Snap;

                return new TableDiscoveryInfo(
                    tableId,
                    r.CdcEnabled,
                    r.HasUniqueKey,
                    r.EstimatedRowCount,
                    suggestedMode);
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Internal DTO for Dapper column mapping from INFORMATION_SCHEMA.COLUMNS.
    /// </summary>
    private sealed class ColumnRow
    {
        public string Name { get; set; } = string.Empty;
        public string SqlType { get; set; } = string.Empty;
        public string IsNullableStr { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public byte? Precision { get; set; }
        public byte? Scale { get; set; }
        public int OrdinalPosition { get; set; }
    }

    /// <summary>
    /// Internal DTO for Dapper mapping of unique key index rows.
    /// </summary>
    private sealed class UniqueKeyRow
    {
        public string IndexName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Internal DTO for Dapper mapping of table discovery rows.
    /// </summary>
    private sealed class TableDiscoveryRow
    {
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public bool CdcEnabled { get; set; }
        public bool HasUniqueKey { get; set; }
        public long EstimatedRowCount { get; set; }
    }
}
