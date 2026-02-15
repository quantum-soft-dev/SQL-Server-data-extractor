using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Models;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Inspects SQL Server metadata to discover tables and retrieve schema manifests.
/// </summary>
public interface ISchemaInspector
{
    Task<SchemaManifest> GetTableMetadataAsync(TableIdentifier table, CancellationToken ct = default);
    Task<IReadOnlyList<TableDiscoveryInfo>> GetAllTablesAsync(CancellationToken ct = default);
}
