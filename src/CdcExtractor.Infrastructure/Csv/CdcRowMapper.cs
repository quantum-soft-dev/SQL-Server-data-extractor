using CdcExtractor.Domain.Models;

namespace CdcExtractor.Infrastructure.Csv;

/// <summary>
/// Maps <see cref="DataRow"/> and <see cref="CdcChangeRow"/> dictionaries to ordered
/// string values suitable for CSV serialization.
/// </summary>
public static class CdcRowMapper
{
    /// <summary>
    /// Extracts ordered string values from a <see cref="DataRow"/> based on column names.
    /// </summary>
    /// <param name="row">The snapshot data row.</param>
    /// <param name="columnNames">Ordered column names defining output order.</param>
    /// <returns>A list of formatted string values in column order.</returns>
    public static IReadOnlyList<string> MapSnapshotRow(DataRow row, IReadOnlyList<string> columnNames)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(columnNames);

        var values = new string[columnNames.Count];

        for (var i = 0; i < columnNames.Count; i++)
        {
            row.Values.TryGetValue(columnNames[i], out var value);
            values[i] = FormatValue(value);
        }

        return values;
    }

    /// <summary>
    /// Extracts ordered string values from a <see cref="CdcChangeRow"/>, prepending
    /// the CDC service columns (<c>_op</c>, <c>_lsn</c>, <c>_seqval</c>, <c>_ts</c>)
    /// before the data columns.
    /// </summary>
    /// <param name="row">The CDC change row.</param>
    /// <param name="columnNames">Ordered data column names (excluding service columns).</param>
    /// <returns>A list of formatted string values: service columns followed by data columns.</returns>
    public static IReadOnlyList<string> MapCdcRow(CdcChangeRow row, IReadOnlyList<string> columnNames)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(columnNames);

        const int serviceColumnCount = 4;
        var values = new string[serviceColumnCount + columnNames.Count];

        values[0] = row.Operation;
        values[1] = row.Lsn;
        values[2] = row.SeqVal;
        values[3] = row.Timestamp.ToString("O");

        for (var i = 0; i < columnNames.Count; i++)
        {
            row.Values.TryGetValue(columnNames[i], out var value);
            values[serviceColumnCount + i] = FormatValue(value);
        }

        return values;
    }

    /// <summary>
    /// Converts a value to its CSV string representation.
    /// </summary>
    /// <remarks>
    /// Conversion rules:
    /// <list type="bullet">
    ///   <item><description><c>null</c> becomes an empty string.</description></item>
    ///   <item><description><c>byte[]</c> is Base64-encoded.</description></item>
    ///   <item><description><see cref="DateTime"/> uses ISO 8601 round-trip format ("O").</description></item>
    ///   <item><description><see cref="DateTimeOffset"/> uses ISO 8601 round-trip format ("O").</description></item>
    ///   <item><description><c>bool</c> becomes lowercase "true" or "false".</description></item>
    ///   <item><description>All other types use <see cref="object.ToString"/>.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="value">The value to format.</param>
    /// <returns>A string representation suitable for CSV output.</returns>
    public static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.ToString("O"),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty,
        };
    }
}
