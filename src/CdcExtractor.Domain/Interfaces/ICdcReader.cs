using CdcExtractor.Domain.Models;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Reads CDC change data and full table snapshots from SQL Server.
/// </summary>
public interface ICdcReader
{
    Task<Lsn> GetMaxLsnAsync(CancellationToken ct = default);
    Task<Lsn> GetMinLsnAsync(string captureInstance, CancellationToken ct = default);
    IAsyncEnumerable<CdcChangeRow> ReadAllChangesAsync(string captureInstance, Lsn from, Lsn to, CancellationToken ct = default);
    IAsyncEnumerable<DataRow> ReadFullTableAsync(TableIdentifier table, CancellationToken ct = default);
}
