using System.Globalization;
using System.IO.Compression;
using System.Text;
using CdcExtractor.Application.Models;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Models;

namespace CdcExtractor.Application.Services;

/// <summary>
/// Converts streams of data rows into gzip-compressed CSV chunks,
/// respecting the configured maximum bytes per chunk.
/// </summary>
public sealed class ChunkingService
{
    private readonly ExtractionConfig _config;

    public ChunkingService(ExtractionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <summary>
    /// Converts an async enumerable of <see cref="DataRow"/> into a list of gzip CSV chunks.
    /// Each chunk respects <see cref="ExtractionConfig.MaxBytesPerChunk"/>.
    /// </summary>
    public async Task<IReadOnlyList<ChunkResult>> ChunkSnapshotRowsAsync(
        IReadOnlyList<string> columnNames,
        IAsyncEnumerable<DataRow> rows,
        CancellationToken ct = default)
    {
        var results = new List<ChunkResult>();
        var chunkNumber = 1;
        var currentRows = new List<DataRow>();
        long estimatedSize = 0;

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false))
        {
            currentRows.Add(row);
            estimatedSize += EstimateRowSize(row, columnNames);

            if (estimatedSize >= _config.MaxBytesPerChunk)
            {
                var chunk = WriteSnapshotChunk(chunkNumber, columnNames, currentRows);
                results.Add(chunk);
                chunkNumber++;
                currentRows.Clear();
                estimatedSize = 0;
            }
        }

        if (currentRows.Count > 0)
        {
            var chunk = WriteSnapshotChunk(chunkNumber, columnNames, currentRows);
            results.Add(chunk);
        }

        return results;
    }

    private ChunkResult WriteSnapshotChunk(
        int chunkNumber, IReadOnlyList<string> columnNames, IReadOnlyList<DataRow> rows)
    {
        var memoryStream = new MemoryStream();

        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzipStream, Encoding.UTF8, leaveOpen: true))
        {
            // Write header
            writer.WriteLine(string.Join(",", columnNames.Select(EscapeCsvField)));

            // Write data rows
            long rowCount = 0;
            foreach (var row in rows)
            {
                var values = columnNames.Select(col =>
                    row.Values.TryGetValue(col, out var val) ? FormatValue(val) : "");
                writer.WriteLine(string.Join(",", values.Select(EscapeCsvField)));
                rowCount++;
            }
        }

        memoryStream.Position = 0;
        return new ChunkResult(chunkNumber, rows.Count, memoryStream.Length, memoryStream);
    }

    private static long EstimateRowSize(DataRow row, IReadOnlyList<string> columnNames)
    {
        long size = 0;
        foreach (var col in columnNames)
        {
            if (row.Values.TryGetValue(col, out var val) && val is not null)
            {
                size += val.ToString()?.Length ?? 0;
            }
            size += 1; // comma separator
        }
        return size + 2; // newline
    }

    internal static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
    }

    internal static string EscapeCsvField(string field)
    {
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
