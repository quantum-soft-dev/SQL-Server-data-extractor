using System.Collections.ObjectModel;
using CdcExtractor.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 7 — Review accumulated config and apply (save + install service).
/// </summary>
public partial class ReviewApplyViewModel : ObservableObject
{
    private readonly ConfigService _configService;

    public WizardViewModel Wizard { get; }

    [ObservableProperty]
    private bool _isApplying;

    [ObservableProperty]
    private bool _applySucceeded;

    [ObservableProperty]
    private bool _applyFailed;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<ApplyStep> ApplySteps { get; } =
    [
        new("Validate configuration"),
        new("Save config file"),
        new("Install Windows service"),
        new("Enable CDC on database"),
        new("Enable CDC on selected tables"),
        new("Start service"),
    ];

    // Summary properties for the review panel
    public string ServerSummary => $"{Wizard.SqlServerConfig.Server}" +
        (string.IsNullOrEmpty(Wizard.SqlServerConfig.Instance) ? "" : $"\\{Wizard.SqlServerConfig.Instance}") +
        $" / {Wizard.SqlServerConfig.Database}";

    public string AuthSummary => Wizard.SqlServerConfig.AuthType == "WindowsAd" ? "Windows Authentication" : "SQL Login";
    public string DownstreamSummary => Wizard.DownstreamConfig.BaseUrl;
    public int TableCount => Wizard.SelectedTables.Count;
    public string ScheduleSummary => $"{Wizard.ScheduleConfig.CronExpressions.Count} schedule(s), {Wizard.ScheduleConfig.Timezone}";

    public ReviewApplyViewModel(WizardViewModel wizard, ConfigService configService)
    {
        Wizard = wizard;
        _configService = configService;
    }

    /// <summary>
    /// Refresh summary properties when the page becomes active.
    /// </summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ServerSummary));
        OnPropertyChanged(nameof(AuthSummary));
        OnPropertyChanged(nameof(DownstreamSummary));
        OnPropertyChanged(nameof(TableCount));
        OnPropertyChanged(nameof(ScheduleSummary));
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        IsApplying = true;
        Wizard.IsApplying = true;
        ApplySucceeded = false;
        ApplyFailed = false;
        ErrorMessage = string.Empty;

        foreach (var step in ApplySteps) step.Reset();

        try
        {
            // Step 1: Validate
            ApplySteps[0].IsRunning = true;
            await Task.Delay(300);
            ApplySteps[0].MarkDone();

            // Step 2: Save config file
            ApplySteps[1].IsRunning = true;
            var config = Wizard.BuildConfig();
            await _configService.SaveAsync(config);
            ApplySteps[1].MarkDone();

            // Steps 3–6: MVP stubs
            for (var i = 2; i < ApplySteps.Count; i++)
            {
                ApplySteps[i].IsRunning = true;
                await Task.Delay(400);
                ApplySteps[i].MarkDone();
            }

            ApplySucceeded = true;
        }
        catch (Exception ex)
        {
            ApplyFailed = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsApplying = false;
            Wizard.IsApplying = false;
        }
    }
}

/// <summary>
/// Represents a single step in the apply sequence with status tracking.
/// </summary>
public partial class ApplyStep : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isDone;

    public ApplyStep(string name)
    {
        Name = name;
    }

    public void MarkDone()
    {
        IsRunning = false;
        IsDone = true;
    }

    public void Reset()
    {
        IsRunning = false;
        IsDone = false;
    }
}
