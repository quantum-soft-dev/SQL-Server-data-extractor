using System.Net;
using System.Text;
using System.Text.Json;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Infrastructure.Http;
using FluentAssertions;

namespace CdcExtractor.Infrastructure.Tests.Http;

public class DeviceFlowAuthenticatorTests
{
    private static readonly DownstreamConfig TestConfig = new()
    {
        BaseUrl = "https://test.example.com",
        ClientId = "test-client-id"
    };

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static DeviceFlowAuthenticator CreateSut(MockHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://auth.example.com")
        };
        return new DeviceFlowAuthenticator(client, TestConfig);
    }

    [Fact]
    public async Task RequestDeviceCodeAsync_ReturnsUserCodeAndVerificationUri()
    {
        // Arrange
        var deviceResponse = JsonSerializer.Serialize(new
        {
            device_code = "device-code-123",
            user_code = "ABCD-1234",
            verification_uri = "https://auth.example.com/activate",
            interval = 5,
            expires_in = 900
        });

        var handler = new MockHttpMessageHandler((request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.PathAndQuery.Should().Contain("device/authorize");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(deviceResponse, Encoding.UTF8, "application/json")
            });
        });

        var sut = CreateSut(handler);

        // Act
        var result = await sut.RequestDeviceCodeAsync();

        // Assert
        result.UserCode.Should().Be("ABCD-1234");
        result.VerificationUri.Should().Be("https://auth.example.com/activate");
        result.DeviceCode.Should().Be("device-code-123");
        result.ExpiresIn.Should().Be(900);
        result.Interval.Should().Be(5);
    }

    [Fact]
    public async Task PollForTokenAsync_AuthorizationPending_ContinuesPollingUntilSuccess()
    {
        // Arrange
        var callCount = 0;
        var handler = new MockHttpMessageHandler((request, _) =>
        {
            callCount++;

            if (callCount <= 2)
            {
                var pendingResponse = JsonSerializer.Serialize(
                    new { error = "authorization_pending" }, SnakeCaseOptions);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(pendingResponse, Encoding.UTF8, "application/json")
                });
            }

            var tokenResponse = JsonSerializer.Serialize(
                new { access_token = "token-after-pending", refresh_token = "refresh-token", expires_in = 3600 },
                SnakeCaseOptions);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tokenResponse, Encoding.UTF8, "application/json")
            });
        });

        var sut = CreateSut(handler);

        // Act
        var result = await sut.PollForTokenAsync("device-code-123", intervalSeconds: 1, expiresIn: 60);

        // Assert
        result.AccessToken.Should().Be("token-after-pending");
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task PollForTokenAsync_RespectsPollingInterval()
    {
        // Arrange
        var callCount = 0;

        var handler = new MockHttpMessageHandler((_, _) =>
        {
            callCount++;

            if (callCount < 3)
            {
                var pendingResponse = JsonSerializer.Serialize(
                    new { error = "authorization_pending" }, SnakeCaseOptions);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(pendingResponse, Encoding.UTF8, "application/json")
                });
            }

            var tokenResponse = JsonSerializer.Serialize(
                new { access_token = "access-token", refresh_token = "refresh-token", expires_in = 3600 },
                SnakeCaseOptions);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tokenResponse, Encoding.UTF8, "application/json")
            });
        });

        var sut = CreateSut(handler);

        // Act
        var before = DateTimeOffset.UtcNow;
        await sut.PollForTokenAsync("device-code-123", intervalSeconds: 1, expiresIn: 30);
        var elapsed = DateTimeOffset.UtcNow - before;

        // Assert — with intervalSeconds=1 and 3 calls (each preceded by a delay), elapsed should be >= 2s
        elapsed.TotalSeconds.Should().BeGreaterThanOrEqualTo(2.0);
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task PollForTokenAsync_ExpiredToken_ThrowsInvalidOperationException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) =>
        {
            var expiredResponse = JsonSerializer.Serialize(
                new { error = "expired_token" }, SnakeCaseOptions);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(expiredResponse, Encoding.UTF8, "application/json")
            });
        });

        var sut = CreateSut(handler);

        // Act
        var act = () => sut.PollForTokenAsync("device-code-123", intervalSeconds: 1, expiresIn: 30);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task PollForTokenAsync_ImmediateSuccess_ReturnsTokenResponse()
    {
        // Arrange
        var tokenResponse = JsonSerializer.Serialize(
            new { access_token = "valid-access-token", refresh_token = "valid-refresh-token", expires_in = 3600 },
            SnakeCaseOptions);

        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tokenResponse, Encoding.UTF8, "application/json")
            }));

        var sut = CreateSut(handler);

        // Act
        var result = await sut.PollForTokenAsync("device-code-123", intervalSeconds: 1, expiresIn: 30);

        // Assert
        result.AccessToken.Should().Be("valid-access-token");
        result.RefreshToken.Should().Be("valid-refresh-token");
        result.ExpiresIn.Should().Be(3600);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await _handler(request, cancellationToken);
        }
    }
}
