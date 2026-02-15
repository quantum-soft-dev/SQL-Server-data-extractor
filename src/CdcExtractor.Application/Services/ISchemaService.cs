using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Inspects table schema and uploads the manifest to the downstream service.
/// </summary>
public interface ISchemaService
{
    Task<SchemaManifest> InspectAndUploadSchemaAsync(
        TableIdentifier table,
        string batchId,
        string leaseToken,
        CancellationToken ct = default);
}
