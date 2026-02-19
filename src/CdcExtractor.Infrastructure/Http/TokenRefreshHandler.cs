using System.Net;
using System.Net.Http.Headers;
using CdcExtractor.Domain.Interfaces;

namespace CdcExtractor.Infrastructure.Http;

/// <summary>
/// DelegatingHandler that injects Bearer tokens from <see cref="ITokenProvider"/>
/// and automatically refreshes on 401 responses or when the token is about to expire.
/// Uses a <see cref="SemaphoreSlim"/> to ensure only one concurrent refresh.
/// </summary>
public sealed class TokenRefreshHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile string? _lastRefreshedToken;

    public TokenRefreshHandler(ITokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Try to get an access token; if expired, refresh proactively
        string token;
        try
        {
            token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Token expired or unavailable — refresh proactively
            token = await RefreshTokenAsync(null, cancellationToken).ConfigureAwait(false);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // 401 received — try to refresh and retry once
        string refreshedToken;
        try
        {
            refreshedToken = await RefreshTokenAsync(token, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Refresh failed — return the original 401 response
            return response;
        }

        // Dispose the original 401 response since we're retrying
        response.Dispose();

        // Clone the request for retry (original request stream may have been consumed)
        using var retryRequest = await CloneRequestAsync(request).ConfigureAwait(false);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedToken);

        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the token, deduplicating concurrent requests.
    /// If another thread already refreshed the token (different from <paramref name="failedToken"/>),
    /// returns the already-refreshed token without calling the provider again.
    /// </summary>
    private async Task<string> RefreshTokenAsync(string? failedToken, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check: if another request already refreshed, use that token
            if (failedToken is not null && _lastRefreshedToken is not null
                && _lastRefreshedToken != failedToken)
            {
                return _lastRefreshedToken;
            }

            var newToken = await _tokenProvider.RefreshAccessTokenAsync(ct).ConfigureAwait(false);
            _lastRefreshedToken = newToken;
            return newToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        if (original.Content is not null)
        {
            var contentBytes = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Version = original.Version;

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshLock.Dispose();
        }
        base.Dispose(disposing);
    }
}
