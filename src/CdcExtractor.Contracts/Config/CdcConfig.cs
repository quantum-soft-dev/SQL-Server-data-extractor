namespace CdcExtractor.Contracts.Config;

public sealed record CdcConfig
{
    public bool AutoEnableDatabase { get; init; } = true;
    public bool AutoEnableTables { get; init; } = true;
    public int RetentionMinDays { get; init; } = 7;
    public int BatchInactivityTtlMinutes { get; init; } = 10;
}
