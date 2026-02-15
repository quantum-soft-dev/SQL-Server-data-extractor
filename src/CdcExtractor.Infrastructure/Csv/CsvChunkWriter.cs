using System.Globalization;
using System.IO.Compression;
using CdcExtractor.Domain.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace CdcExtractor.Infrastructure.Csv;

/// <summary>
/// Writes snapshot and CDC change rows to gzip-compressed CSV chunks using CsvHelper
/// for RFC 4180 compliant output.
/// </summary>
public sealed class CsvChunkWriter
{
    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
    };

    /// <summary>
    /// Writes snapshot rows to a gzip-compressed CSV stream.
    /// The returned <see cref="MemoryStream"/> is positioned at the beginning and
    /// must be disposed by the caller.
    /// </summary>
    /// <param name="columnNames">Ordered column names for the header row.</param>
    /// <param name="rows">Snapshot rows to write.</param>
    /// <returns>A <see cref="MemoryStream"/> containing the gzip-compressed CSV data.</returns>
    public MemoryStream WriteSnapshotChunk(IReadOnlyList<string> columnNames, IReadOnlyList<DataRow> rows)
    {
        ArgumentNullException.ThrowIfNull(columnNames);
        ArgumentNullException.ThrowIfNull(rows);

        var ms = new MemoryStream();
        try
        {
            using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            using (var writer = new StreamWriter(gzip, leaveOpen: true))
            using (var csv = new CsvWriter(writer, CsvConfig))
            {
                WriteHeader(csv, columnNames);
                csv.NextRecord();

                foreach (var row in rows)
                {
                    var values = CdcRowMapper.MapSnapshotRow(row, columnNames);
                    WriteFields(csv, values);
                    csv.NextRecord();
                }
            }

            ms.Position = 0;
            return ms;
        }
        catch
        {
            ms.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Writes CDC change rows to a gzip-compressed CSV stream.
    /// Service columns (<c>_op</c>, <c>_lsn</c>, <c>_seqval</c>, <c>_ts</c>) are
    /// prepended before data columns.
    /// The returned <see cref="MemoryStream"/> is positioned at the beginning and
    /// must be disposed by the caller.
    /// </summary>
    /// <param name="columnNames">Ordered data column names (excluding service columns).</param>
    /// <param name="rows">CDC change rows to write.</param>
    /// <returns>A <see cref="MemoryStream"/> containing the gzip-compressed CSV data.</returns>
    public MemoryStream WriteCdcChunk(IReadOnlyList<string> columnNames, IReadOnlyList<CdcChangeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(columnNames);
        ArgumentNullException.ThrowIfNull(rows);

        var ms = new MemoryStream();
        try
        {
            using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            using (var writer = new StreamWriter(gzip, leaveOpen: true))
            using (var csv = new CsvWriter(writer, CsvConfig))
            {
                // Write header: service columns + data columns
                string[] serviceColumns = ["_op", "_lsn", "_seqval", "_ts"];
                WriteHeader(csv, serviceColumns);
                WriteHeader(csv, columnNames);
                csv.NextRecord();

                foreach (var row in rows)
                {
                    var values = CdcRowMapper.MapCdcRow(row, columnNames);
                    WriteFields(csv, values);
                    csv.NextRecord();
                }
            }

            ms.Position = 0;
            return ms;
        }
        catch
        {
            ms.Dispose();
            throw;
        }
    }

    private static void WriteHeader(CsvWriter csv, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            csv.WriteField(name);
        }
    }

    private static void WriteFields(CsvWriter csv, IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            csv.WriteField(value);
        }
    }
}
