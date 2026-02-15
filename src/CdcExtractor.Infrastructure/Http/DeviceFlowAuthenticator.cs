using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CdcExtractor.Contracts.Config;

namespace CdcExtractor.Infrastructure.Http;

/// <summary>
/// Result of requesting a device code in the OAuth 2.0 Device Authorization Grant (RFC 8628).
/// </summary>
public sealed record DeviceCodeResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

/// <summary>
/// Result of a successful token exchange or refresh.
/// </summary>
public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

/// <summary>
/// Implements the OAuth 2.0 Device Authorization Grant (RFC 8628) for authenticating
/// with the downstream service. Handles the full device flow lifecycle: requesting a
/// device code, polling for authorization, and refreshing tokens.
/// </summary>
public sealed class DeviceFlowAuthenticator
{
    private readonly HttpClient _httpClient;
    private readonly DownstreamConfig _config;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Initializes a new instance of <see cref="DeviceFlowAuthenticator"/>.
    /// </summary>
    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> should be set
    /// to <see cref="DownstreamConfig.BaseUrl"/>.
    /// </param>
    /// <param name="config">Downstream service configuration.</param>
    public DeviceFlowAuthenticator(HttpClient httpClient, DownstreamConfig config)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(config);

        _httpClient = httpClient;
        _config = config;
    }

    /// <summary>
    /// Requests a device code from the authorization server.
    /// This is Step 1 of the device flow: the application obtains a device code and
    /// user code that must be presented to the user for out-of-band authorization.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DeviceCodeResponse"/> containing the device code, user code, and verification URI.</returns>
    /// <exception cref="HttpRequestException">Thrown when the authorization server returns a non-success status code.</exception>
    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken ct = default)
    {
        var content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("client_id", _config.ClientId),
            new("scope", "extractor"),
        });

        using var response = await _httpClient.PostAsync("device/authorize", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DeviceCodeResponseDto>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new JsonException("Failed to deserialize device code response.");

        return new DeviceCodeResponse(
            result.DeviceCode,
            result.UserCode,
            result.VerificationUri,
            result.ExpiresIn,
            result.Interval);
    }

    /// <summary>
    /// Polls the token endpoint until the user authorizes the device, the code expires,
    /// or cancellation is requested. This is Step 2 of the device flow.
    /// </summary>
    /// <param name="deviceCode">The device code obtained from <see cref="RequestDeviceCodeAsync"/>.</param>
    /// <param name="intervalSeconds">Minimum polling interval in seconds.</param>
    /// <param name="expiresIn">Maximum time in seconds before the device code expires.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="TokenResponse"/> upon successful authorization.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the device code expires before user authorization.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="HttpRequestException">Thrown on unexpected HTTP errors.</exception>
    public async Task<TokenResponse> PollForTokenAsync(
        string deviceCode,
        int intervalSeconds,
        int expiresIn,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        var interval = TimeSpan.FromSeconds(Math.Max(intervalSeconds, 1));

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(interval, ct).ConfigureAwait(false);

            var content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
            {
                new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                new("device_code", deviceCode),
                new("client_id", _config.ClientId),
            });

            using var response = await _httpClient.PostAsync("token", content, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await ReadTokenResponseAsync(response, ct).ConfigureAwait(false);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var error = await ReadErrorResponseAsync(response, ct).ConfigureAwait(false);

                if (string.Equals(error, "expired_token", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The device code has expired. Please restart the authentication flow.");
                }

                if (string.Equals(error, "authorization_pending", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(error, "slow_down", StringComparison.Ordinal))
                {
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                }

                throw new HttpRequestException($"Token endpoint returned error: {error}", null, HttpStatusCode.BadRequest);
            }

            response.EnsureSuccessStatusCode();
        }

        throw new InvalidOperationException("The device code has expired. Please restart the authentication flow.");
    }

    /// <summary>
    /// Refreshes an existing access token using the refresh token grant.
    /// </summary>
    /// <param name="refreshToken">The refresh token obtained from a previous token exchange.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new <see cref="TokenResponse"/> with a fresh access token.</returns>
    /// <exception cref="HttpRequestException">Thrown when the token endpoint returns a non-success status code.</exception>
    public async Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
            new("client_id", _config.ClientId),
        });

        using var response = await _httpClient.PostAsync("token", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await ReadTokenResponseAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task<TokenResponse> ReadTokenResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var result = await response.Content.ReadFromJsonAsync<TokenResponseDto>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new JsonException("Failed to deserialize token response.");

        return new TokenResponse(result.AccessToken, result.RefreshToken, result.ExpiresIn);
    }

    private static async Task<string> ReadErrorResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(JsonOptions, ct).ConfigureAwait(false);
        return error?.Error ?? string.Empty;
    }

    /// <summary>
    /// Internal DTO for deserializing device code responses with snake_case JSON.
    /// </summary>
    private sealed class DeviceCodeResponseDto
    {
        public string DeviceCode { get; set; } = string.Empty;
        public string UserCode { get; set; } = string.Empty;
        public string VerificationUri { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public int Interval { get; set; }
    }

    /// <summary>
    /// Internal DTO for deserializing token responses with snake_case JSON.
    /// </summary>
    private sealed class TokenResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
    }

    /// <summary>
    /// Internal DTO for deserializing error responses with snake_case JSON.
    /// </summary>
    private sealed class ErrorResponseDto
    {
        public string Error { get; set; } = string.Empty;
    }
}
