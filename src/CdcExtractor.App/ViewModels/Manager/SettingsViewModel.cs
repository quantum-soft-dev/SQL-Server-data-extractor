using System.Collections.ObjectModel;
using CdcExtractor.App.Services;
using CdcExtractor.Contracts.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels;

/// <summary>
/// ViewModel for the Manager Settings page.
/// Loads and saves the application configuration (config.json) via ConfigService.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _configService;

    // SQL Server
    [ObservableProperty]
    private string _server = string.Empty;

    [ObservableProperty]
    private string? _instance;

    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    private string _authType = "WindowsAd";

    // Downstream
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    // Schedule
    [ObservableProperty]
    private string _timezone = "UTC";

    [ObservableProperty]
    private string _newCronExpression = string.Empty;

    [ObservableProperty]
    private string? _selectedCronExpression;

    public ObservableCollection<string> CronExpressions { get; } = [];

    // CDC
    [ObservableProperty]
    private bool _autoEnableDatabase = true;

    [ObservableProperty]
    private bool _autoEnableTables = true;

    [ObservableProperty]
    private int _retentionMinDays = 7;

    [ObservableProperty]
    private int _batchInactivityTtlMinutes = 10;

    // Extraction
    [ObservableProperty]
    private int _maxBytesPerChunk = 10_485_760;

    [ObservableProperty]
    private int _smallTableSnapThreshold = 1000;

    // Status
    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    public IReadOnlyList<string> AuthTypes { get; } = ["WindowsAd", "SqlLogin"];

    public SettingsViewModel(ConfigService configService)
    {
        ArgumentNullException.ThrowIfNull(configService);
        _configService = configService;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            IsBusy = true;
            StatusMessage = null;

            var config = await _configService.LoadAsync().ConfigureAwait(false);
            if (config is null)
            {
                StatusMessage = "No configuration file found. Using defaults.";
                return;
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // SQL Server
                Server = config.SqlServer.Server;
                Instance = config.SqlServer.Instance;
                Database = config.SqlServer.Database;
                AuthType = config.SqlServer.AuthType;

                // Downstream
                BaseUrl = config.Downstream.BaseUrl;
                ClientId = config.Downstream.ClientId;

                // Schedule
                Timezone = config.Schedule.Timezone;
                CronExpressions.Clear();
                foreach (var expr in config.Schedule.CronExpressions)
                {
                    CronExpressions.Add(expr);
                }

                // CDC
                AutoEnableDatabase = config.Cdc.AutoEnableDatabase;
                AutoEnableTables = config.Cdc.AutoEnableTables;
                RetentionMinDays = config.Cdc.RetentionMinDays;
                BatchInactivityTtlMinutes = config.Cdc.BatchInactivityTtlMinutes;

                // Extraction
                MaxBytesPerChunk = config.Extraction.MaxBytesPerChunk;
                SmallTableSnapThreshold = config.Extraction.SmallTableSnapThreshold;
            });

            StatusMessage = "Configuration loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            IsBusy = true;
            StatusMessage = null;

            var config = new AppConfig
            {
                SqlServer = new SqlServerConfig
                {
                    Server = Server,
                    Instance = Instance,
                    Database = Database,
                    AuthType = AuthType,
                },
                Downstream = new DownstreamConfig
                {
                    BaseUrl = BaseUrl,
                    ClientId = ClientId,
                },
                Schedule = new ScheduleConfig
                {
                    CronExpressions = CronExpressions.ToList(),
                    Timezone = Timezone,
                },
                Cdc = new CdcConfig
                {
                    AutoEnableDatabase = AutoEnableDatabase,
                    AutoEnableTables = AutoEnableTables,
                    RetentionMinDays = RetentionMinDays,
                    BatchInactivityTtlMinutes = BatchInactivityTtlMinutes,
                },
                Extraction = new ExtractionConfig
                {
                    MaxBytesPerChunk = MaxBytesPerChunk,
                    SmallTableSnapThreshold = SmallTableSnapThreshold,
                },
            };

            await _configService.SaveAsync(config).ConfigureAwait(false);
            StatusMessage = "Configuration saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddCron()
    {
        var expr = NewCronExpression.Trim();
        if (!string.IsNullOrWhiteSpace(expr) && !CronExpressions.Contains(expr))
        {
            CronExpressions.Add(expr);
            NewCronExpression = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveCron()
    {
        if (SelectedCronExpression is not null)
        {
            CronExpressions.Remove(SelectedCronExpression);
            SelectedCronExpression = null;
        }
    }
}
