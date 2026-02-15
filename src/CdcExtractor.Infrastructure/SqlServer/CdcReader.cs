using System.Data;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.Models;
using CdcExtractor.Domain.ValueObjects;
using Dapper;
using Microsoft.Data.SqlClient;
using DataRow = CdcExtractor.Domain.Models.DataRow;

namespace CdcExtractor.Infrastructure.SqlServer;

/// <summary>
/// Reads CDC change data and full table snapshots from SQL Server using Dapper and raw ADO.NET for streaming.
/// </summary>
public sealed partial class CdcReader : ICdcReader
{
    private readonly SqlConnectionFactory _connectionFactory;

    private const string GetMaxLsnSql = "SELECT sys.fn_cdc_get_max_lsn();";
    private const string GetMinLsnSql = "SELECT sys.fn_cdc_get_min_lsn(@captureInstance);";

    /// <summary>
    /// Regex for validating capture instance names to prevent SQL injection.
    /// Only alphanumeric characters and underscores are allowed.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9_]+$")]
    private static partial Regex SafeCaptureInstancePattern();

    public CdcReader(SqlConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<Lsn> GetMaxLsnAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        var result = await connection.QuerySingleAsync<byte[]?>(
            new CommandDefinition(GetMaxLsnSql, cancellationToken: ct))
            .ConfigureAwait(false);

        return result is { Length: 10 }
            ? Lsn.From(result)
            : Lsn.Empty;
    }

    public async Task<Lsn> GetMinLsnAsync(string captureInstance, CancellationToken ct = default)
    {
        ValidateCaptureInstance(captureInstance);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        var result = await connection.QuerySingleAsync<byte[]?>(
            new CommandDefinition(GetMinLsnSql,
                new { captureInstance },
                cancellationToken: ct))
            .ConfigureAwait(false);

        return result is { Length: 10 }
            ? Lsn.From(result)
            : Lsn.Empty;
    }

    public async IAsyncEnumerable<CdcChangeRow> ReadAllChangesAsync(
        string captureInstance,
        Lsn from,
        Lsn to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ValidateCaptureInstance(captureInstance);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        // The capture instance is validated above, so it is safe to interpolate into the function name.
        // CDC table-valued functions require the capture instance in the function name and cannot be parameterized.
        var sql = $"SELECT * FROM cdc.fn_cdc_get_all_changes_{captureInstance}(@from, @to, N'all update old')";

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@from", SqlDbType.Binary, 10) { Value = from.Value });
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.Binary, 10) { Value = to.Value });

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return MapChangeRow(reader);
        }
    }

    public async IAsyncEnumerable<DataRow> ReadFullTableAsync(
        TableIdentifier table,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        // Set snapshot isolation for a consistent read
        await using (var isolationCommand = connection.CreateCommand())
        {
            isolationCommand.CommandText = "SET TRANSACTION ISOLATION LEVEL SNAPSHOT;";
            await isolationCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table.QuotedFullName};";

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return MapDataRow(reader);
        }
    }

    /// <summary>
    /// Maps a single row from the CDC all-changes function to a <see cref="CdcChangeRow"/>.
    /// </summary>
    private static CdcChangeRow MapChangeRow(SqlDataReader reader)
    {
        var operationCode = reader.GetInt32(reader.GetOrdinal("__$operation"));
        var operation = operationCode switch
        {
            1 => "D",
            2 => "I",
            3 => "UO", // Update old values (before image)
            4 => "UN", // Update new values (after image)
            _ => operationCode.ToString()
        };

        var lsnOrdinal = reader.GetOrdinal("__$start_lsn");
        var lsnBytes = new byte[10];
        reader.GetBytes(lsnOrdinal, 0, lsnBytes, 0, 10);
        var lsn = Lsn.From(lsnBytes).ToString();

        var seqValOrdinal = reader.GetOrdinal("__$seqval");
        var seqValBytes = new byte[10];
        reader.GetBytes(seqValOrdinal, 0, seqValBytes, 0, 10);
        var seqVal = Lsn.From(seqValBytes).ToString();

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            // Skip CDC system columns
            if (columnName.StartsWith("__$", StringComparison.Ordinal))
                continue;

            values[columnName] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return new CdcChangeRow(
            operation,
            lsn,
            seqVal,
            DateTimeOffset.UtcNow,
            values);
    }

    /// <summary>
    /// Maps a single row from a full table scan to a <see cref="DataRow"/>.
    /// </summary>
    private static DataRow MapDataRow(SqlDataReader reader)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return new DataRow(values);
    }

    /// <summary>
    /// Validates that a capture instance name contains only safe characters to prevent SQL injection.
    /// </summary>
    private static void ValidateCaptureInstance(string captureInstance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureInstance);

        if (!SafeCaptureInstancePattern().IsMatch(captureInstance))
        {
            throw new ArgumentException(
                "Capture instance name contains invalid characters. Only alphanumeric characters and underscores are allowed.",
                nameof(captureInstance));
        }
    }
}
