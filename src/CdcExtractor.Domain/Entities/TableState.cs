using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Entities;

/// <summary>
/// Aggregate root for per-table persistent state tracking.
/// Manages extraction mode, LSN progress, bootstrap status, and schema hash.
/// </summary>
public sealed class TableState
{
    public TableIdentifier TableId { get; }
    public ExtractionMode ExtractionMode { get; }
    public Lsn LastProcessedLsn { get; private set; }
    public BootstrapStatus BootstrapStatus { get; private set; }
    public SchemaHash? SchemaHash { get; private set; }
    public DateTimeOffset? LastSyncTime { get; private set; }
    public string? CaptureInstance { get; set; }
    public string? ErrorMessage { get; private set; }

    public TableState(TableIdentifier tableId, ExtractionMode mode)
    {
        ArgumentNullException.ThrowIfNull(tableId);

        TableId = tableId;
        ExtractionMode = mode;
        BootstrapStatus = BootstrapStatus.Pending;
        LastProcessedLsn = Lsn.Empty;
    }

    /// <summary>
    /// Marks this table as complete after a successful extraction.
    /// Can only be called when status is <see cref="BootstrapStatus.Pending"/>
    /// or <see cref="BootstrapStatus.ReBootstrap"/>.
    /// </summary>
    public void MarkComplete(Lsn processedLsn, SchemaHash hash, DateTimeOffset syncTime)
    {
        ArgumentNullException.ThrowIfNull(processedLsn);
        ArgumentNullException.ThrowIfNull(hash);

        if (BootstrapStatus is not (BootstrapStatus.Pending or BootstrapStatus.ReBootstrap))
        {
            throw new InvalidOperationException(
                $"Cannot mark complete from status '{BootstrapStatus}'. " +
                $"Expected '{BootstrapStatus.Pending}' or '{BootstrapStatus.ReBootstrap}'.");
        }

        BootstrapStatus = BootstrapStatus.Complete;
        LastProcessedLsn = processedLsn;
        SchemaHash = hash;
        LastSyncTime = syncTime;
        ErrorMessage = null;
    }

    /// <summary>
    /// Flags this table for re-bootstrap. Can only be called when status is
    /// <see cref="BootstrapStatus.Complete"/>.
    /// </summary>
    public void MarkReBootstrap(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (BootstrapStatus != BootstrapStatus.Complete)
        {
            throw new InvalidOperationException(
                $"Cannot mark re-bootstrap from status '{BootstrapStatus}'. " +
                $"Expected '{BootstrapStatus.Complete}'.");
        }

        BootstrapStatus = BootstrapStatus.ReBootstrap;
        ErrorMessage = reason;
    }

    /// <summary>Sets an error message on this table state.</summary>
    public void SetError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ErrorMessage = message;
    }

    /// <summary>Clears any error message on this table state.</summary>
    public void ClearError()
    {
        ErrorMessage = null;
    }
}
