using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;
using Dapper;

namespace CdcExtractor.Infrastructure.SqlServer;

/// <summary>
/// Manages CDC configuration on SQL Server databases and tables using Dapper.
/// </summary>
public sealed class CdcManager : ICdcManager
{
    private readonly SqlConnectionFactory _connectionFactory;

    private const string IsCdcEnabledOnDatabaseSql = """
        SELECT CAST(is_cdc_enabled AS BIT)
        FROM sys.databases
        WHERE name = DB_NAME();
        """;

    private const string EnableCdcOnDatabaseSql = "EXEC sys.sp_cdc_enable_db;";

    private const string IsCdcEnabledOnTableSql = """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @Schema
              AND t.name = @Name
              AND t.is_tracked_by_cdc = 1
        ) THEN 1 ELSE 0 END AS BIT);
        """;

    private const string EnableCdcOnTableSql = """
        EXEC sys.sp_cdc_enable_table
            @source_schema   = @Schema,
            @source_name     = @Name,
            @capture_instance = @CaptureInstance,
            @supports_net_changes = 0,
            @role_name       = NULL;
        """;

    private const string GetRetentionMinutesSql = """
        SELECT retention
        FROM msdb.dbo.cdc_jobs
        WHERE job_type = N'cleanup';
        """;

    private const string SetRetentionMinutesSql = """
        EXEC sys.sp_cdc_change_job
            @jobtype   = N'cleanup',
            @retention = @Minutes;
        """;

    private const string IsSqlAgentRunningSql = """
        SELECT CAST(CASE
            WHEN EXISTS (
                SELECT 1
                FROM sys.dm_server_services
                WHERE servicename LIKE N'SQL Server Agent%'
                  AND status = 4
            ) THEN 1 ELSE 0 END AS BIT);
        """;

    public CdcManager(SqlConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> IsCdcEnabledOnDatabaseAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        return await connection.QuerySingleAsync<bool>(
            new CommandDefinition(IsCdcEnabledOnDatabaseSql, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task EnableCdcOnDatabaseAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(EnableCdcOnDatabaseSql, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task<bool> IsCdcEnabledOnTableAsync(TableIdentifier table, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        return await connection.QuerySingleAsync<bool>(
            new CommandDefinition(IsCdcEnabledOnTableSql,
                new { table.Schema, table.Name },
                cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task<string> EnableCdcOnTableAsync(TableIdentifier table, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        var captureInstance = $"{table.Schema}_{table.Name}";

        if (captureInstance.Length > 100)
        {
            throw new ArgumentException(
                $"Capture instance name '{captureInstance}' is {captureInstance.Length} characters, " +
                "which exceeds the SQL Server limit of 100 characters for sys.sp_cdc_enable_table @capture_instance. " +
                $"Shorten the schema or table name for {table.FullName}.",
                nameof(table));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(EnableCdcOnTableSql,
                new { table.Schema, table.Name, CaptureInstance = captureInstance },
                cancellationToken: ct))
            .ConfigureAwait(false);

        return captureInstance;
    }

    public async Task<int> GetRetentionMinutesAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        return await connection.QuerySingleAsync<int>(
            new CommandDefinition(GetRetentionMinutesSql, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task SetRetentionMinutesAsync(int minutes, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minutes, 0);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(
            new CommandDefinition(SetRetentionMinutesSql,
                new { Minutes = minutes },
                cancellationToken: ct))
            .ConfigureAwait(false);
    }

    public async Task<bool> IsSqlAgentRunningAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        return await connection.QuerySingleAsync<bool>(
            new CommandDefinition(IsSqlAgentRunningSql, cancellationToken: ct))
            .ConfigureAwait(false);
    }
}
