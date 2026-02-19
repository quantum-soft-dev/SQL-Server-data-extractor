namespace CdcExtractor.Contracts.Config;

public sealed record SqlServerConfig
{
    public string Server { get; init; } = string.Empty;
    public string? Instance { get; init; }
    public string Database { get; init; } = string.Empty;
    public string AuthType { get; init; } = "WindowsAd";
    public bool Encrypt { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
}
