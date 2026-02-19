using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using CdcExtractor.Domain.Interfaces;

namespace CdcExtractor.Infrastructure.Http;

/// <summary>
/// Stores and retrieves OAuth tokens encrypted with DPAPI (LocalMachine scope).
/// Tokens are persisted as Base64-encoded encrypted bytes in a local file.
/// LocalMachine scope allows both the WPF wizard (interactive user) and the
/// Windows Service (service account) to encrypt/decrypt the same token file.
/// The token file should be protected with restrictive NTFS ACLs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiTokenStore : ITokenProvider
{
    private readonly string _tokenFilePath;
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public DpapiTokenStore(string tokenFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenFilePath);
        _tokenFilePath = tokenFilePath;
    }

    /// <summary>
    /// Returns the current access token if it hasn't expired, otherwise refreshes.
    /// </summary>
    public Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return Task.FromResult(_accessToken);
        }

        // If no in-memory token, try to load refresh token and signal that a refresh is needed
        throw new InvalidOperationException(
            "Access token has expired or is not available. Call RefreshAccessTokenAsync or re-authenticate.");
    }

    /// <summary>
    /// Not directly implemented here — the actual refresh is performed by
    /// <see cref="DeviceFlowAuthenticator"/> and the result stored via <see cref="StoreTokensAsync"/>.
    /// </summary>
    public Task<string> RefreshAccessTokenAsync(CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "Use DeviceFlowAuthenticator.RefreshTokenAsync and then call StoreTokensAsync.");
    }

    /// <summary>
    /// Stores the access token in memory and the refresh token encrypted on disk.
    /// </summary>
    public async Task StoreTokensAsync(string accessToken, string refreshToken, int expiresInSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        _accessToken = accessToken;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);

        var plainBytes = Encoding.UTF8.GetBytes(refreshToken);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.LocalMachine);
        var base64 = Convert.ToBase64String(encryptedBytes);

        var directory = Path.GetDirectoryName(_tokenFilePath);
        if (directory is not null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_tokenFilePath, base64).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the encrypted refresh token from disk and decrypts it using DPAPI.
    /// </summary>
    public async Task<string?> LoadRefreshTokenAsync()
    {
        if (!File.Exists(_tokenFilePath))
        {
            return null;
        }

        var base64 = await File.ReadAllTextAsync(_tokenFilePath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        var encryptedBytes = Convert.FromBase64String(base64);
        var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Removes the stored token file from disk.
    /// </summary>
    public void DeleteTokenFile()
    {
        if (File.Exists(_tokenFilePath))
        {
            File.Delete(_tokenFilePath);
        }
    }

    /// <summary>
    /// Sets the in-memory access token and expiry (used after refresh).
    /// </summary>
    public void SetAccessToken(string accessToken, int expiresInSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        _accessToken = accessToken;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
    }
}
