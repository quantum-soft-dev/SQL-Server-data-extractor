namespace CdcExtractor.Domain.Exceptions;

/// <summary>
/// Thrown when the downstream service is unreachable or returns a non-recoverable transport error.
/// Wraps infrastructure-level transport exceptions (e.g. <c>HttpRequestException</c>) so the
/// Application layer can classify batch-level errors without depending on <c>System.Net.Http</c>.
/// </summary>
public sealed class DownstreamUnavailableException : Exception
{
    public DownstreamUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
