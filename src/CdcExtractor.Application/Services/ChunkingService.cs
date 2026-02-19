using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using CdcExtractor.Application.Models;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Models;

using DataRow = CdcExtractor.Domain.Models.DataRow;

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
    /// Converts an async enumerable of <see cref="DataRow"/> into a streaming sequence of gzip CSV chunks.
    /// Each chunk respects <see cref="ExtractionConfig.MaxBytesPerChunk"/>.
    /// Callers should upload and dispose each chunk as it is produced to keep memory usage
    /// proportional to one chunk at a time.
    /// </summary>
    public async IAsyncEnumerable<ChunkResult> ChunkSnapshotRowsAsync(
        IReadOnlyList<string> columnNames,
        IAsyncEnumerable<DataRow> rows,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chunkNumber = 1;
        var currentRows = new List<DataRow>();
        long estimatedSize = 0;

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false))
        {
            currentRows.Add(row);
            estimatedSize += EstimateRowSize(row, columnNames);

            if (estimatedSize >= _config.MaxBytesPerChunk)
            {
                yield return WriteSnapshotChunk(chunkNumber, columnNames, currentRows);
                chunkNumber++;
                currentRows.Clear();
                estimatedSize = 0;
            }
        }

        if (currentRows.Count > 0)
        {
            yield return WriteSnapshotChunk(chunkNumber, columnNames, currentRows);
        }
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

    /// <summary>
    /// Converts an async enumerable of <see cref="CdcChangeRow"/> into a streaming sequence of gzip CSV chunks.
    /// Includes service columns: _op, _lsn, _seqval, _ts.
    /// </summary>
    public async IAsyncEnumerable<ChunkResult> ChunkCdcRowsAsync(
        IReadOnlyList<string> columnNames,
        IAsyncEnumerable<CdcChangeRow> rows,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chunkNumber = 1;
        var currentRows = new List<CdcChangeRow>();
        long estimatedSize = 0;

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false))
        {
            currentRows.Add(row);
            estimatedSize += EstimateCdcRowSize(row, columnNames);

            if (estimatedSize >= _config.MaxBytesPerChunk)
            {
                yield return WriteCdcChunk(chunkNumber, columnNames, currentRows);
                chunkNumber++;
                currentRows.Clear();
                estimatedSize = 0;
            }
        }

        if (currentRows.Count > 0)
        {
            yield return WriteCdcChunk(chunkNumber, columnNames, currentRows);
        }
    }

    private ChunkResult WriteCdcChunk(
        int chunkNumber, IReadOnlyList<string> columnNames, IReadOnlyList<CdcChangeRow> rows)
    {
        var memoryStream = new MemoryStream();

        // Service columns prepended to the header
        var serviceColumns = new[] { "_op", "_lsn", "_seqval", "_ts" };
        var allColumns = serviceColumns.Concat(columnNames).ToList();

        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzipStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteLine(string.Join(",", allColumns.Select(EscapeCsvField)));

            foreach (var row in rows)
            {
                var serviceValues = new[]
                {
                    EscapeCsvField(row.Operation),
                    EscapeCsvField(row.Lsn),
                    EscapeCsvField(row.SeqVal),
                    EscapeCsvField(row.Timestamp.ToString("O", CultureInfo.InvariantCulture))
                };
                var dataValues = columnNames.Select(col =>
                    row.Values.TryGetValue(col, out var val) ? FormatValue(val) : "")
                    .Select(EscapeCsvField);
                writer.WriteLine(string.Join(",", serviceValues.Concat(dataValues)));
            }
        }

        memoryStream.Position = 0;
        return new ChunkResult(chunkNumber, rows.Count, memoryStream.Length, memoryStream);
    }

    private static long EstimateCdcRowSize(CdcChangeRow row, IReadOnlyList<string> columnNames)
    {
        long size = row.Operation.Length + row.Lsn.Length + row.SeqVal.Length + 30; // service cols
        foreach (var col in columnNames)
        {
            if (row.Values.TryGetValue(col, out var val) && val is not null)
            {
                size += val.ToString()?.Length ?? 0;
            }
            size += 1;
        }
        return size + 2;
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
