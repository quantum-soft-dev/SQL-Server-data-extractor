using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Infrastructure.Http;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CdcExtractor.Infrastructure.Tests.Http;

public class TokenRefreshHandlerTests
{
    private readonly ITokenProvider _tokenProvider;

    public TokenRefreshHandlerTests()
    {
        _tokenProvider = Substitute.For<ITokenProvider>();
    }

    private (HttpClient client, MockHttpMessageHandler inner) CreateClient(
        params HttpResponseMessage[] responses)
    {
        var inner = new MockHttpMessageHandler(responses);
        var handler = new TokenRefreshHandler(_tokenProvider)
        {
            InnerHandler = inner
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        return (client, inner);
    }

    [Fact]
    public async Task SendAsync_InjectsBearerToken()
    {
        // Arrange
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("test-access-token");

        var (client, inner) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await client.GetAsync("/api/data");

        // Assert
        inner.SentRequests.Should().HaveCount(1);
        inner.SentRequests[0].Headers.Authorization
            .Should().BeEquivalentTo(new AuthenticationHeaderValue("Bearer", "test-access-token"));
    }

    [Fact]
    public async Task SendAsync_On401_RefreshesTokenAndRetries()
    {
        // Arrange
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("expired-token", "refreshed-token");
        _tokenProvider.RefreshAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("refreshed-token");

        var (client, inner) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var response = await client.GetAsync("/api/data");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.SentRequests.Should().HaveCount(2);

        // First request should use expired token
        inner.SentRequests[0].Headers.Authorization!.Parameter.Should().Be("expired-token");

        // Retry should use refreshed token
        inner.SentRequests[1].Headers.Authorization!.Parameter.Should().Be("refreshed-token");

        await _tokenProvider.Received(1).RefreshAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_On401_RefreshFails_ReturnsOriginal401()
    {
        // Arrange
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("expired-token");
        _tokenProvider.RefreshAccessTokenAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Refresh failed"));

        var unauthorizedResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var (client, inner) = CreateClient(unauthorizedResponse);

        // Act
        var response = await client.GetAsync("/api/data");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        inner.SentRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_ConcurrentRequests_OnlyOneRefresh()
    {
        // Arrange — all concurrent requests get the same "expired-token"
        // so the double-check deduplication can identify them as the same failed token
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("expired-token");
        _tokenProvider.RefreshAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                // Simulate a slow refresh to ensure concurrent requests overlap
                await Task.Delay(200);
                return "refreshed-token";
            });

        // 3 pairs of (401, OK) for 3 concurrent requests that each get a 401 then retry
        var inner = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new HttpResponseMessage(HttpStatusCode.OK),
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new HttpResponseMessage(HttpStatusCode.OK),
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new HttpResponseMessage(HttpStatusCode.OK));

        var handler = new TokenRefreshHandler(_tokenProvider)
        {
            InnerHandler = inner
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };

        // Act — fire three concurrent requests that all get 401
        var tasks = new[]
        {
            client.GetAsync("/api/data1"),
            client.GetAsync("/api/data2"),
            client.GetAsync("/api/data3")
        };
        await Task.WhenAll(tasks);

        // Assert — double-check pattern in RefreshTokenAsync should ensure only one refresh call
        await _tokenProvider.Received(1).RefreshAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ProactiveRefresh_WhenGetAccessTokenThrows()
    {
        // Arrange — GetAccessTokenAsync throws (e.g., DpapiTokenStore when token is expired),
        // so the handler falls back to RefreshAccessTokenAsync before sending the request.
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(
                "Access token has expired or is not available. Call RefreshAccessTokenAsync or re-authenticate."));
        _tokenProvider.RefreshAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("proactively-refreshed-token");

        var (client, inner) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var response = await client.GetAsync("/api/data");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.SentRequests.Should().HaveCount(1);
        inner.SentRequests[0].Headers.Authorization!.Parameter
            .Should().Be("proactively-refreshed-token");

        await _tokenProvider.Received(1).RefreshAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_SuccessfulRequest_NoRefresh()
    {
        // Arrange
        _tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns("valid-token");

        var (client, inner) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var response = await client.GetAsync("/api/data");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.SentRequests.Should().HaveCount(1);

        await _tokenProvider.DidNotReceive()
            .RefreshAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<HttpResponseMessage> _responses = new();
        private readonly ConcurrentQueue<HttpRequestMessage> _sentRequests = new();
        public IReadOnlyList<HttpRequestMessage> SentRequests => [.. _sentRequests];

        public MockHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            foreach (var r in responses)
                _responses.Enqueue(r);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _sentRequests.Enqueue(request);
            return Task.FromResult(_responses.TryDequeue(out var response)
                ? response
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
