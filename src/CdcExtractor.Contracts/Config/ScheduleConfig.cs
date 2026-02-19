namespace CdcExtractor.Contracts.Config;

public sealed record ScheduleConfig
{
    public IReadOnlyList<string> CronExpressions { get; init; } = [];
    public string Timezone { get; init; } = "UTC";
    public bool PreventParallelRuns { get; init; } = true;
}
