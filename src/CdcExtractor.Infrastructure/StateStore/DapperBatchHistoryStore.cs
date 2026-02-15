using System.Reflection;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;
using CdcExtractor.Infrastructure.SqlServer;
using Dapper;

namespace CdcExtractor.Infrastructure.StateStore;

/// <summary>
/// Dapper-based implementation of <see cref="IBatchHistoryStore"/>.
/// Persists <see cref="BatchRun"/> and <see cref="DatasetRun"/> entities
/// to the <c>__ExtractorBatchHistory</c> and <c>__ExtractorDatasetHistory</c> tables.
/// </summary>
public sealed class DapperBatchHistoryStore : IBatchHistoryStore
{
    private readonly SqlConnectionFactory _connectionFactory;

    private const string InsertBatchSql = """
        INSERT INTO dbo.__ExtractorBatchHistory
            (BatchId, RemoteBatchId, BatchType, [Trigger], Status, LeaseToken, StartedAt, FinishedAt)
        VALUES
            (@BatchId, @RemoteBatchId, @BatchType, @Trigger, @Status, @LeaseToken, @StartedAt, @FinishedAt);
        """;

    private const string InsertDatasetSql = """
        INSERT INTO dbo.__ExtractorDatasetHistory
            (DatasetId, BatchId, RemoteDatasetId, TableSchema, TableName,
             FromLsn, ToLsn, Status, RowCount, ChunkCount, BytesSent, ErrorCode, ErrorMessage)
        VALUES
            (@DatasetId, @BatchId, @RemoteDatasetId, @TableSchema, @TableName,
             @FromLsn, @ToLsn, @Status, @RowCount, @ChunkCount, @BytesSent, @ErrorCode, @ErrorMessage);
        """;

    private const string UpdateBatchStatusSql = """
        UPDATE dbo.__ExtractorBatchHistory
        SET Status = @Status, FinishedAt = @FinishedAt
        WHERE BatchId = @BatchId;
        """;

    private const string SelectRecentBatchesSql = """
        SELECT TOP (@Limit)
            BatchId, RemoteBatchId, BatchType, [Trigger], Status, LeaseToken, StartedAt, FinishedAt
        FROM dbo.__ExtractorBatchHistory
        ORDER BY StartedAt DESC;
        """;

    private const string SelectBatchByIdSql = """
        SELECT BatchId, RemoteBatchId, BatchType, [Trigger], Status, LeaseToken, StartedAt, FinishedAt
        FROM dbo.__ExtractorBatchHistory
        WHERE BatchId = @BatchId;
        """;

    private const string SelectDatasetsByBatchIdSql = """
        SELECT DatasetId, BatchId, RemoteDatasetId, TableSchema, TableName,
               FromLsn, ToLsn, Status, RowCount, ChunkCount, BytesSent, ErrorCode, ErrorMessage
        FROM dbo.__ExtractorDatasetHistory
        WHERE BatchId = @BatchId;
        """;

    private const string SelectDatasetsByBatchIdsSql = """
        SELECT DatasetId, BatchId, RemoteDatasetId, TableSchema, TableName,
               FromLsn, ToLsn, Status, RowCount, ChunkCount, BytesSent, ErrorCode, ErrorMessage
        FROM dbo.__ExtractorDatasetHistory
        WHERE BatchId IN @BatchIds;
        """;

    public DapperBatchHistoryStore(SqlConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task SaveBatchAsync(BatchRun batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(InsertBatchSql, new
            {
                BatchId = batch.Id.Value,
                batch.RemoteBatchId,
                BatchType = batch.Type.ToString(),
                Trigger = batch.Trigger.ToString(),
                Status = batch.Status.ToString(),
                batch.LeaseToken,
                batch.StartedAt,
                batch.FinishedAt,
            }, transaction, cancellationToken: ct)).ConfigureAwait(false);

            foreach (var dataset in batch.Datasets)
            {
                await connection.ExecuteAsync(new CommandDefinition(InsertDatasetSql, new
                {
                    DatasetId = dataset.Id.Value,
                    BatchId = batch.Id.Value,
                    dataset.RemoteDatasetId,
                    TableSchema = dataset.TableId.Schema,
                    TableName = dataset.TableId.Name,
                    FromLsn = dataset.FromLsn?.Value,
                    ToLsn = dataset.ToLsn?.Value,
                    Status = dataset.Status.ToString(),
                    dataset.RowCount,
                    dataset.ChunkCount,
                    dataset.BytesSent,
                    dataset.ErrorCode,
                    dataset.ErrorMessage,
                }, transaction, cancellationToken: ct)).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task UpdateBatchStatusAsync(BatchId id, BatchStatus status, DateTimeOffset? finishedAt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(UpdateBatchStatusSql, new
        {
            BatchId = id.Value,
            Status = status.ToString(),
            FinishedAt = finishedAt,
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BatchRun>> GetRecentBatchesAsync(int limit, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        var batchRows = (await connection.QueryAsync<BatchRow>(
            new CommandDefinition(SelectRecentBatchesSql, new { Limit = limit }, cancellationToken: ct))
            .ConfigureAwait(false)).ToList();

        if (batchRows.Count == 0)
        {
            return Array.Empty<BatchRun>();
        }

        var batchIds = batchRows.Select(b => b.BatchId).ToArray();
        var datasetRows = (await connection.QueryAsync<DatasetRow>(
            new CommandDefinition(SelectDatasetsByBatchIdsSql, new { BatchIds = batchIds }, cancellationToken: ct))
            .ConfigureAwait(false)).ToList();

        var datasetsByBatch = datasetRows.GroupBy(d => d.BatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return batchRows
            .Select(b => MapToBatchEntity(b, datasetsByBatch.GetValueOrDefault(b.BatchId, [])))
            .ToList()
            .AsReadOnly();
    }

    public async Task<BatchRun?> GetBatchByIdAsync(BatchId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);

        var batchRow = await connection.QuerySingleOrDefaultAsync<BatchRow>(
            new CommandDefinition(SelectBatchByIdSql, new { BatchId = id.Value }, cancellationToken: ct))
            .ConfigureAwait(false);

        if (batchRow is null)
        {
            return null;
        }

        var datasetRows = (await connection.QueryAsync<DatasetRow>(
            new CommandDefinition(SelectDatasetsByBatchIdSql, new { BatchId = id.Value }, cancellationToken: ct))
            .ConfigureAwait(false)).ToList();

        return MapToBatchEntity(batchRow, datasetRows);
    }

    /// <summary>
    /// Reconstitutes a <see cref="BatchRun"/> from database rows using reflection
    /// to access the private constructor and setters.
    /// </summary>
    private static BatchRun MapToBatchEntity(BatchRow row, List<DatasetRow> datasetRows)
    {
        var batchType = Enum.Parse<BatchType>(row.BatchType);
        var trigger = Enum.Parse<BatchTrigger>(row.Trigger);

        // Use reflection to call the private constructor
        var batch = (BatchRun)Activator.CreateInstance(
            typeof(BatchRun),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: [new BatchId(row.BatchId), batchType, trigger, row.StartedAt],
            culture: null)!;

        // Set properties via reflection that are only settable through domain methods
        SetProperty(batch, nameof(BatchRun.Status), Enum.Parse<BatchStatus>(row.Status));
        SetProperty(batch, nameof(BatchRun.FinishedAt), row.FinishedAt);
        SetProperty(batch, nameof(BatchRun.RemoteBatchId), row.RemoteBatchId);
        SetProperty(batch, nameof(BatchRun.LeaseToken), row.LeaseToken);

        // Add datasets via the internal list
        var datasetsField = typeof(BatchRun).GetField("_datasets", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Field '_datasets' not found on BatchRun.");

        var datasets = (List<DatasetRun>)datasetsField.GetValue(batch)!;

        foreach (var dsRow in datasetRows)
        {
            datasets.Add(MapToDatasetEntity(dsRow));
        }

        return batch;
    }

    /// <summary>
    /// Reconstitutes a <see cref="DatasetRun"/> from a database row using reflection.
    /// </summary>
    private static DatasetRun MapToDatasetEntity(DatasetRow row)
    {
        var tableId = new TableIdentifier(row.TableSchema, row.TableName);
        var fromLsn = row.FromLsn is { Length: 10 } fromBytes ? Lsn.From(fromBytes) : null;
        var toLsn = row.ToLsn is { Length: 10 } toBytes ? Lsn.From(toBytes) : null;

        // Use reflection to call the private constructor
        var dataset = (DatasetRun)Activator.CreateInstance(
            typeof(DatasetRun),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: [new DatasetId(row.DatasetId), tableId, fromLsn, toLsn],
            culture: null)!;

        SetProperty(dataset, nameof(DatasetRun.Status), Enum.Parse<DatasetStatus>(row.Status));
        SetProperty(dataset, nameof(DatasetRun.RowCount), row.RowCount);
        SetProperty(dataset, nameof(DatasetRun.ChunkCount), row.ChunkCount);
        SetProperty(dataset, nameof(DatasetRun.BytesSent), row.BytesSent);
        SetProperty(dataset, nameof(DatasetRun.RemoteDatasetId), row.RemoteDatasetId);
        SetProperty(dataset, nameof(DatasetRun.ErrorCode), row.ErrorCode);
        SetProperty(dataset, nameof(DatasetRun.ErrorMessage), row.ErrorMessage);

        return dataset;
    }

    private static void SetProperty<TEntity>(TEntity target, string propertyName, object? value) where TEntity : class
    {
        var property = typeof(TEntity).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {typeof(TEntity).Name}.");

        property.SetValue(target, value);
    }

    /// <summary>Dapper row DTO for __ExtractorBatchHistory.</summary>
    private sealed class BatchRow
    {
        public Guid BatchId { get; set; }
        public string? RemoteBatchId { get; set; }
        public string BatchType { get; set; } = string.Empty;
        public string Trigger { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? LeaseToken { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
    }

    /// <summary>Dapper row DTO for __ExtractorDatasetHistory.</summary>
    private sealed class DatasetRow
    {
        public Guid DatasetId { get; set; }
        public Guid BatchId { get; set; }
        public string? RemoteDatasetId { get; set; }
        public string TableSchema { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public byte[]? FromLsn { get; set; }
        public byte[]? ToLsn { get; set; }
        public string Status { get; set; } = string.Empty;
        public long RowCount { get; set; }
        public int ChunkCount { get; set; }
        public long BytesSent { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
