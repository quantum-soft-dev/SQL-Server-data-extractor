namespace CdcExtractor.Contracts.Config;

public sealed record DownstreamConfig
{
    public string BaseUrl { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; init; } = 120;
}
