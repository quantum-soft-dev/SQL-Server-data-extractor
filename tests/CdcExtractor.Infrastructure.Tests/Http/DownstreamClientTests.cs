using System.Net;
using System.Text;
using System.Text.Json;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;
using CdcExtractor.Infrastructure.Http;
using FluentAssertions;

namespace CdcExtractor.Infrastructure.Tests.Http;

public class DownstreamClientTests
{
    private static DownstreamClient CreateSut(MockHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.example.com")
        };
        return new DownstreamClient(client);
    }

    [Fact]
    public async Task CreateBatchAsync_SendsPostToBatchesEndpoint_ReturnsBatchIdAndLeaseToken()
    {
        // Arrange
        var responseBody = JsonSerializer.Serialize(new
        {
            batchId = "batch-123",
            leaseToken = "lease-abc"
        });

        var handler = new MockHttpMessageHandler(async (request, ct) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.PathAndQuery.Should().Be("/v1/batches");

            var body = await request.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("SNAPSHOT");
            body.Should().Contain("localhost");
            body.Should().Contain("TestDb");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        });

        var sut = CreateSut(handler);

        // Act
        var (batchId, leaseToken) = await sut.CreateBatchAsync(
            BatchType.Snapshot, "localhost", "TestDb");

        // Assert
        batchId.Should().Be("batch-123");
        leaseToken.Should().Be("lease-abc");
    }

    [Fact]
    public async Task CreateDatasetAsync_SendsPostWithBatchAndTableInfo()
    {
        // Arrange
        var responseBody = JsonSerializer.Serialize(new { datasetId = "ds-456" });

        var handler = new MockHttpMessageHandler(async (request, ct) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.PathAndQuery.Should().Be("/v1/datasets");

            var body = await request.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("batch-123");
            body.Should().Contain("dbo.Orders");
            body.Should().Contain("hash-xyz");

            request.Headers.GetValues("X-Batch-Lease").Should().Contain("lease-abc");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        });

        var sut = CreateSut(handler);

        // Act
        var datasetId = await sut.CreateDatasetAsync(
            "batch-123", "lease-abc", "dbo.Orders", null, null, "hash-xyz");

        // Assert
        datasetId.Should().Be("ds-456");
    }

    [Fact]
    public async Task UploadChunkAsync_SendsStreamContentWithGzipEncoding()
    {
        // Arrange
        var chunkData = new byte[] { 0x1F, 0x8B, 0x08, 0x00 };
        using var chunkStream = new MemoryStream(chunkData);

        var handler = new MockHttpMessageHandler(async (request, ct) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.PathAndQuery.Should().Contain("/v1/datasets/ds-456/chunks");

            var contentType = request.Content!.Headers.ContentType?.MediaType;
            contentType.Should().Be("application/octet-stream");

            request.Content.Headers.ContentEncoding.Should().Contain("gzip");

            request.Headers.GetValues("X-Chunk-No").Should().Contain("1");
            request.Headers.GetValues("X-Batch-Lease").Should().Contain("lease-abc");

            var sentBytes = await request.Content.ReadAsByteArrayAsync(ct);
            sentBytes.Should().BeEquivalentTo(chunkData);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var sut = CreateSut(handler);

        // Act
        await sut.UploadChunkAsync("ds-456", "lease-abc", 1, chunkStream);

        // Assert
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task CommitDatasetAsync_SendsPostToCommitEndpoint()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.PathAndQuery.Should().Be("/v1/datasets/ds-456:commit");
            request.Headers.GetValues("X-Batch-Lease").Should().Contain("lease-abc");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var sut = CreateSut(handler);

        // Act
        await sut.CommitDatasetAsync("ds-456", "lease-abc");

        // Assert
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FinishBatchAsync_SendsPostWithUpperCaseStatus()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(async (request, ct) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.PathAndQuery.Should().Contain("/v1/batches/batch-123:finish");

            var body = await request.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("SUCCEEDED");
            body.Should().Contain("\"tablesTotal\":5");
            body.Should().Contain("\"totalRows\":10000");

            request.Headers.GetValues("X-Batch-Lease").Should().Contain("lease-abc");

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var sut = CreateSut(handler);

        // Act
        await sut.FinishBatchAsync(
            "batch-123", "lease-abc", BatchStatus.Succeeded,
            tablesTotal: 5, tablesSucceeded: 5, totalRows: 10000, totalBytes: 5242880);

        // Assert
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task UploadSchemaAsync_SendsPutToSchemasEndpoint()
    {
        // Arrange
        var manifest = new SchemaManifest(
            new TableIdentifier("dbo", "Orders"),
            DateTimeOffset.UtcNow,
            new SchemaHash("abc123def456abc123def456abc123def456abc123def456abc123def456abc1"),
            [],
            new List<string> { "OrderId" },
            [],
            null);

        var handler = new MockHttpMessageHandler(async (request, ct) =>
        {
            request.Method.Should().Be(HttpMethod.Put);
            request.RequestUri!.PathAndQuery.Should().Contain("/v1/tables/");
            request.RequestUri.PathAndQuery.Should().Contain("/schemas/");

            var body = await request.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("dbo.Orders");
            body.Should().Contain("OrderId");

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var sut = CreateSut(handler);

        // Act
        await sut.UploadSchemaAsync("dbo.Orders", "hash-xyz", manifest);

        // Assert
        handler.CallCount.Should().Be(1);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public int CallCount { get; private set; }

        public MockHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return await _handler(request, cancellationToken);
        }
    }
}
