using System.IO;
using System.Text.Json;
using CdcExtractor.Contracts.Config;

namespace CdcExtractor.App.Services;

public sealed class ConfigService
{
    private static readonly string DefaultConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SQLExtractor", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string ConfigPath { get; }

    public ConfigService(string? configPath = null)
    {
        ConfigPath = configPath ?? DefaultConfigPath;
    }

    public async Task<AppConfig?> LoadAsync()
    {
        if (!File.Exists(ConfigPath)) return null;
        var json = await File.ReadAllTextAsync(ConfigPath).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
    }

    public async Task SaveAsync(AppConfig config)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (directory is not null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(ConfigPath, json).ConfigureAwait(false);
    }

    public bool ConfigExists() => File.Exists(ConfigPath);
}
