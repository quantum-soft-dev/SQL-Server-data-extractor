using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Retrieves table schema manifests, computes schema hashes,
/// and uploads schemas to the downstream service.
/// </summary>
public sealed class SchemaService : ISchemaService
{
    private readonly ISchemaInspector _schemaInspector;
    private readonly IDownstreamClient _downstreamClient;

    public SchemaService(ISchemaInspector schemaInspector, IDownstreamClient downstreamClient)
    {
        ArgumentNullException.ThrowIfNull(schemaInspector);
        ArgumentNullException.ThrowIfNull(downstreamClient);

        _schemaInspector = schemaInspector;
        _downstreamClient = downstreamClient;
    }

    /// <inheritdoc />
    public async Task<SchemaManifest> InspectAndUploadSchemaAsync(
        TableIdentifier table,
        string batchId,
        string leaseToken,
        CancellationToken ct = default)
    {
        var manifest = await _schemaInspector.GetTableMetadataAsync(table, ct).ConfigureAwait(false);

        await _downstreamClient.UploadSchemaAsync(
            table.FullName, manifest.Hash.Value, manifest, ct).ConfigureAwait(false);

        return manifest;
    }

    /// <summary>
    /// Checks if the schema has changed by comparing hashes.
    /// </summary>
    public bool HasSchemaChanged(SchemaHash? previousHash, SchemaHash currentHash)
    {
        ArgumentNullException.ThrowIfNull(currentHash);

        if (previousHash is null)
        {
            return true;
        }

        return !previousHash.Equals(currentHash);
    }
}
