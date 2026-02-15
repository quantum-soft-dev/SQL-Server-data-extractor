using System.Reflection;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;
using CdcExtractor.Infrastructure.SqlServer;
using Dapper;

namespace CdcExtractor.Infrastructure.StateStore;

/// <summary>
/// Dapper-based implementation of <see cref="IStateStore"/>.
/// Persists <see cref="TableState"/> entities to the <c>__ExtractorTableStates</c> table.
/// </summary>
public sealed class DapperStateStore : IStateStore
{
    private readonly SqlConnectionFactory _connectionFactory;

    private const string SelectByPkSql = """
        SELECT TableSchema, TableName, ExtractionMode, LastProcessedLsn,
               BootstrapStatus, SchemaHash, CaptureInstance, LastSyncTime, ErrorMessage
        FROM dbo.__ExtractorTableStates
        WHERE TableSchema = @TableSchema AND TableName = @TableName;
        """;

    private const string SelectAllSql = """
        SELECT TableSchema, TableName, ExtractionMode, LastProcessedLsn,
               BootstrapStatus, SchemaHash, CaptureInstance, LastSyncTime, ErrorMessage
        FROM dbo.__ExtractorTableStates;
        """;

    private const string UpsertSql = """
        MERGE dbo.__ExtractorTableStates AS target
        USING (SELECT @TableSchema AS TableSchema, @TableName AS TableName) AS source
            ON target.TableSchema = source.TableSchema AND target.TableName = source.TableName
        WHEN MATCHED THEN
            UPDATE SET
                ExtractionMode   = @ExtractionMode,
                LastProcessedLsn = @LastProcessedLsn,
                BootstrapStatus  = @BootstrapStatus,
                SchemaHash       = @SchemaHash,
                CaptureInstance  = @CaptureInstance,
                LastSyncTime     = @LastSyncTime,
                ErrorMessage     = @ErrorMessage,
                UpdatedAt        = SYSDATETIMEOFFSET()
        WHEN NOT MATCHED THEN
            INSERT (TableSchema, TableName, ExtractionMode, LastProcessedLsn,
                    BootstrapStatus, SchemaHash, CaptureInstance, LastSyncTime, ErrorMessage, UpdatedAt)
            VALUES (@TableSchema, @TableName, @ExtractionMode, @LastProcessedLsn,
                    @BootstrapStatus, @SchemaHash, @CaptureInstance, @LastSyncTime, @ErrorMessage, SYSDATETIMEOFFSET());
        """;

    private const string DeleteSql = """
        DELETE FROM dbo.__ExtractorTableStates
        WHERE TableSchema = @TableSchema AND TableName = @TableName;
        """;

    public DapperStateStore(SqlConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<TableState?> GetTableStateAsync(TableIdentifier tableId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tableId);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<TableStateRow>(
            new CommandDefinition(SelectByPkSql, new { TableSchema = tableId.Schema, TableName = tableId.Name }, cancellationToken: ct))
            .ConfigureAwait(false);

        return row is null ? null : MapToEntity(row);
    }

    public async Task<IReadOnlyList<TableState>> GetAllTableStatesAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<TableStateRow>(
            new CommandDefinition(SelectAllSql, cancellationToken: ct))
            .ConfigureAwait(false);

        return rows.Select(MapToEntity).ToList().AsReadOnly();
    }

    public async Task UpsertTableStateAsync(TableState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        var lsnValue = state.LastProcessedLsn == Lsn.Empty ? null : state.LastProcessedLsn.Value;

        await connection.ExecuteAsync(new CommandDefinition(UpsertSql, new
        {
            TableSchema = state.TableId.Schema,
            TableName = state.TableId.Name,
            ExtractionMode = state.ExtractionMode.ToString(),
            LastProcessedLsn = lsnValue,
            BootstrapStatus = state.BootstrapStatus.ToString(),
            SchemaHash = state.SchemaHash?.Value,
            state.CaptureInstance,
            state.LastSyncTime,
            state.ErrorMessage,
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteTableStateAsync(TableIdentifier tableId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tableId);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(DeleteSql,
            new { TableSchema = tableId.Schema, TableName = tableId.Name },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Reconstitutes a <see cref="TableState"/> domain entity from a database row.
    /// Uses reflection to set private-setter properties that have no public mutator.
    /// </summary>
    private static TableState MapToEntity(TableStateRow row)
    {
        var mode = Enum.Parse<ExtractionMode>(row.ExtractionMode);
        var tableId = new TableIdentifier(row.TableSchema, row.TableName);
        var state = new TableState(tableId, mode);

        // Set properties that are only settable via domain methods or reflection
        SetPrivateProperty(state, nameof(TableState.BootstrapStatus), Enum.Parse<BootstrapStatus>(row.BootstrapStatus));
        SetPrivateProperty(state, nameof(TableState.LastProcessedLsn),
            row.LastProcessedLsn is { Length: 10 } bytes ? Lsn.From(bytes) : Lsn.Empty);
        SetPrivateProperty(state, nameof(TableState.SchemaHash),
            row.SchemaHash is not null ? new SchemaHash(row.SchemaHash) : null);
        SetPrivateProperty(state, nameof(TableState.LastSyncTime), row.LastSyncTime);
        SetPrivateProperty(state, nameof(TableState.ErrorMessage), row.ErrorMessage);

        state.CaptureInstance = row.CaptureInstance;

        return state;
    }

    private static void SetPrivateProperty<T>(TableState target, string propertyName, T value)
    {
        var property = typeof(TableState).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on TableState.");

        property.SetValue(target, value);
    }

    /// <summary>
    /// Internal DTO for Dapper row mapping. Column names match the SQL table exactly.
    /// </summary>
    private sealed class TableStateRow
    {
        public string TableSchema { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string ExtractionMode { get; set; } = string.Empty;
        public byte[]? LastProcessedLsn { get; set; }
        public string BootstrapStatus { get; set; } = string.Empty;
        public string? SchemaHash { get; set; }
        public string? CaptureInstance { get; set; }
        public DateTimeOffset? LastSyncTime { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
