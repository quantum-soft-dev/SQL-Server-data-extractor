namespace CdcExtractor.Domain.Exceptions;

/// <summary>
/// Thrown when an upload to the downstream sink service fails.
/// </summary>
public sealed class SinkUploadException : Exception
{
    public string? DatasetId { get; }
    public string? Table { get; }
    public int HttpStatusCode { get; }

    public SinkUploadException(string? datasetId, string? table, int httpStatusCode, string message)
        : base(message)
    {
        DatasetId = datasetId;
        Table = table;
        HttpStatusCode = httpStatusCode;
    }

    public SinkUploadException(string? datasetId, string? table, int httpStatusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        DatasetId = datasetId;
        Table = table;
        HttpStatusCode = httpStatusCode;
    }
}
