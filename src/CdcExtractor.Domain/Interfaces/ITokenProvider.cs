namespace CdcExtractor.Domain.Interfaces;

/// <summary>
/// Provides and refreshes access tokens for downstream service authentication.
/// </summary>
public interface ITokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
    Task<string> RefreshAccessTokenAsync(CancellationToken ct = default);
}
