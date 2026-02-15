using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Persists and retrieves per-table extraction state.
/// </summary>
public interface IStateStore
{
    Task<TableState?> GetTableStateAsync(TableIdentifier tableId, CancellationToken ct = default);
    Task<IReadOnlyList<TableState>> GetAllTableStatesAsync(CancellationToken ct = default);
    Task UpsertTableStateAsync(TableState state, CancellationToken ct = default);
    Task DeleteTableStateAsync(TableIdentifier tableId, CancellationToken ct = default);
}
