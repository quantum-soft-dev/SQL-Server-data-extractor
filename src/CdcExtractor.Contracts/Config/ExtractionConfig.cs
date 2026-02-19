namespace CdcExtractor.Contracts.Config;

public sealed record ExtractionConfig
{
    public int MaxBytesPerChunk { get; init; } = 10_485_760;
    public int SmallTableSnapThreshold { get; init; } = 1000;
}
