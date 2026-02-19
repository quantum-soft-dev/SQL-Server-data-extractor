using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Exceptions;
using CdcExtractor.Domain.Interfaces;

namespace CdcExtractor.Infrastructure.Http;

/// <summary>
/// HTTP client for the downstream data ingestion service.
/// Implements the full batch/dataset lifecycle per downstream-api-client.md.
/// </summary>
public sealed class DownstreamClient : IDownstreamClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public DownstreamClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<(string batchId, string leaseToken)> CreateBatchAsync(
        BatchType type, string sqlServer, string database, CancellationToken ct = default)
    {
        var body = new
        {
            source = new { sqlServer, database },
            trigger = "SCHEDULED",
            type = type.ToString().ToUpperInvariant(),
        };

        using var request = CreateJsonRequest(HttpMethod.Post, "/v1/batches", body);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);

        var result = await DeserializeAsync<CreateBatchResponse>(response, ct).ConfigureAwait(false);
        return (result.BatchId, result.LeaseToken);
    }

    public async Task HeartbeatAsync(string batchId, string leaseToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/batches/{batchId}:heartbeat");
        request.Headers.Add("X-Batch-Lease", leaseToken);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task FinishBatchAsync(
        string batchId, string leaseToken, BatchStatus status,
        int tablesTotal, int tablesSucceeded, long totalRows, long totalBytes,
        CancellationToken ct = default)
    {
        var body = new
        {
            status = status.ToString().ToUpperInvariant(),
            summary = new { tablesTotal, tablesSucceeded, totalRows, totalBytes },
        };

        using var request = CreateJsonRequest(HttpMethod.Post, $"/v1/batches/{batchId}:finish", body);
        request.Headers.Add("X-Batch-Lease", leaseToken);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task ReportErrorAsync(
        string batchId, string leaseToken, string scope, string? table, string? datasetId,
        string severity, string code, string message, bool isRetryable, bool isTerminal,
        CancellationToken ct = default)
    {
        var body = new
        {
            occurredAt = DateTimeOffset.UtcNow,
            scope,
            table,
            datasetId,
            severity,
            code,
            message,
            isRetryable,
            isTerminal,
        };

        using var request = CreateJsonRequest(HttpMethod.Post, $"/v1/batches/{batchId}/errors", body);
        request.Headers.Add("X-Batch-Lease", leaseToken);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<string> CreateDatasetAsync(
        string batchId, string leaseToken, string table,
        string? fromLsn, string? toLsn, string schemaHash,
        CancellationToken ct = default)
    {
        var body = new
        {
            batchId,
            table,
            fromLsn,
            toLsn,
            schemaHash,
        };

        using var request = CreateJsonRequest(HttpMethod.Post, "/v1/datasets", body);
        request.Headers.Add("X-Batch-Lease", leaseToken);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);

        var result = await DeserializeAsync<CreateDatasetResponse>(response, ct).ConfigureAwait(false);
        return result.DatasetId;
    }

    public async Task UploadChunkAsync(
        string datasetId, string leaseToken, int chunkNo, Stream gzipCsv,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/datasets/{datasetId}/chunks");
        request.Headers.Add("X-Batch-Lease", leaseToken);
        request.Headers.Add("X-Chunk-No", chunkNo.ToString());

        var content = new StreamContent(gzipCsv);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentEncoding.Add("gzip");
        request.Content = content;

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task CommitDatasetAsync(string datasetId, string leaseToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/datasets/{datasetId}:commit");
        request.Headers.Add("X-Batch-Lease", leaseToken);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task AbortDatasetAsync(string datasetId, string leaseToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/datasets/{datasetId}:abort");
        request.Headers.Add("X-Batch-Lease", leaseToken);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task UploadSchemaAsync(
        string table, string schemaHash, SchemaManifest manifest, CancellationToken ct = default)
    {
        var body = new
        {
            table = manifest.Table.FullName,
            capturedAt = manifest.CapturedAt,
            schemaHash,
            columns = manifest.Columns.Select(c => new
            {
                name = c.Name,
                sqlType = c.SqlType,
                nullable = c.IsNullable,
                maxLength = c.MaxLength,
                precision = c.Precision,
                scale = c.Scale,
                ordinalPosition = c.OrdinalPosition,
            }),
            primaryKey = manifest.PrimaryKey,
            uniqueKeys = manifest.UniqueKeys,
            watermarkColumn = manifest.WatermarkColumn,
        };

        var encodedTable = Uri.EscapeDataString(table);
        var encodedHash = Uri.EscapeDataString(schemaHash);

        using var request = CreateJsonRequest(HttpMethod.Put, $"/v1/tables/{encodedTable}/schemas/{encodedHash}", body);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string uri, object body)
    {
        var request = new HttpRequestMessage(method, uri);
        var json = JsonSerializer.Serialize(body, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new DownstreamUnavailableException(
                $"Downstream service unreachable for {request.Method} {request.RequestUri?.AbsolutePath}: " +
                $"{ex.Message}. Check network connectivity and downstream service availability.", ex);
        }

        var statusCode = (int)response.StatusCode;

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new SinkUploadException(
                null, null, statusCode,
                "Lease conflict (409): batch has been superseded by another instance. " +
                "Another extractor may be running against the same source. " +
                "Verify single-instance deployment.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var classification = statusCode switch
            {
                400 => "Validation error (non-retryable). Check request payload.",
                401 => "Authentication failed. Token may have expired or been revoked.",
                403 => "Forbidden. Verify the extractor client has the required permissions.",
                404 => "Resource not found. Verify the downstream API base URL and resource IDs.",
                429 => "Rate limited (transient). Request will be retried automatically.",
                >= 500 and < 600 => "Server error (transient). Downstream service may be temporarily unavailable.",
                _ => "Unexpected error.",
            };

            throw new SinkUploadException(
                null, null, statusCode,
                $"Downstream API {request.Method} {request.RequestUri?.AbsolutePath} returned {statusCode}: " +
                $"{classification} Response: {errorBody}");
        }

        return response;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Downstream API returned null response body.");
    }

    private sealed record CreateBatchResponse(
        [property: JsonPropertyName("batchId")] string BatchId,
        [property: JsonPropertyName("leaseToken")] string LeaseToken);

    private sealed record CreateDatasetResponse(
        [property: JsonPropertyName("datasetId")] string DatasetId);
}
