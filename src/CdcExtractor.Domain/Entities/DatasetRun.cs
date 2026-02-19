using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Entities;

/// <summary>
/// Represents the extraction of a single table within a <see cref="BatchRun"/>.
/// Tracks upload progress, row counts, and terminal status.
/// </summary>
public sealed class DatasetRun
{
    public DatasetId Id { get; }
    public string? RemoteDatasetId { get; private set; }
    public TableIdentifier TableId { get; }
    public Lsn? FromLsn { get; }
    public Lsn? ToLsn { get; }
    public DatasetStatus Status { get; private set; }
    public long RowCount { get; private set; }
    public int ChunkCount { get; private set; }
    public long BytesSent { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    private DatasetRun(DatasetId id, TableIdentifier tableId, Lsn? fromLsn, Lsn? toLsn)
    {
        Id = id;
        TableId = tableId;
        FromLsn = fromLsn;
        ToLsn = toLsn;
        Status = DatasetStatus.Created;
    }

    /// <summary>
    /// Creates a new <see cref="DatasetRun"/> with a generated ID and Created status.
    /// </summary>
    public static DatasetRun Create(TableIdentifier tableId, Lsn? fromLsn, Lsn? toLsn)
    {
        ArgumentNullException.ThrowIfNull(tableId);
        return new DatasetRun(DatasetId.New(), tableId, fromLsn, toLsn);
    }

    /// <summary>Transitions the dataset to Uploading status.</summary>
    public void MarkUploading()
    {
        Status = DatasetStatus.Uploading;
    }

    /// <summary>Increments the chunk count and accumulates bytes sent.</summary>
    public void IncrementChunk(long bytes)
    {
        ChunkCount++;
        BytesSent += bytes;
    }

    /// <summary>Increments the total row count.</summary>
    public void IncrementRows(long count)
    {
        RowCount += count;
    }

    /// <summary>Marks the dataset as committed (successfully uploaded).</summary>
    public void Commit()
    {
        Status = DatasetStatus.Committed;
    }

    /// <summary>Marks the dataset as aborted with an error code and message.</summary>
    public void Abort(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        Status = DatasetStatus.Aborted;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Marks the dataset as skipped with a reason.</summary>
    public void Skip(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = DatasetStatus.Skipped;
        ErrorMessage = reason;
    }

    /// <summary>Sets the remote dataset identifier returned by the downstream service.</summary>
    public void SetRemoteDatasetId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        RemoteDatasetId = id;
    }
}
