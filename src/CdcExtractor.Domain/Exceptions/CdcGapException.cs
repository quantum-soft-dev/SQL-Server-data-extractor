using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Exceptions;

/// <summary>
/// Thrown when a gap is detected in the CDC log sequence numbers,
/// indicating that change data has been purged before it was consumed.
/// </summary>
public sealed class CdcGapException : Exception
{
    public TableIdentifier Table { get; }
    public Lsn ExpectedLsn { get; }
    public Lsn MinAvailableLsn { get; }

    public CdcGapException(TableIdentifier table, Lsn expectedLsn, Lsn minAvailableLsn, string message)
        : base(message)
    {
        Table = table;
        ExpectedLsn = expectedLsn;
        MinAvailableLsn = minAvailableLsn;
    }

    public CdcGapException(TableIdentifier table, Lsn expectedLsn, Lsn minAvailableLsn, string message, Exception innerException)
        : base(message, innerException)
    {
        Table = table;
        ExpectedLsn = expectedLsn;
        MinAvailableLsn = minAvailableLsn;
    }
}
