using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 9 — Final summary page showing setup results and next steps.
/// </summary>
public partial class DoneViewModel : ObservableObject
{
    public WizardViewModel Wizard { get; }

    [ObservableProperty]
    private string _serviceStatus = "Running";

    [ObservableProperty]
    private string _nextScheduledRun = "Pending...";

    public string ServerSummary => $"{Wizard.SqlServerConfig.Server}" +
        (string.IsNullOrEmpty(Wizard.SqlServerConfig.Instance) ? "" : $"\\{Wizard.SqlServerConfig.Instance}") +
        $" / {Wizard.SqlServerConfig.Database}";

    public int TableCount => Wizard.SelectedTables.Count;
    public int ScheduleCount => Wizard.ScheduleConfig.CronExpressions.Count;

    public DoneViewModel(WizardViewModel wizard)
    {
        Wizard = wizard;
    }

    /// <summary>
    /// Refresh status from the service (MVP stub).
    /// </summary>
    public async Task RefreshStatusAsync()
    {
        // MVP stub — would query IpcClient for real status
        await Task.Delay(200);
        ServiceStatus = "Running";
        NextScheduledRun = Wizard.ScheduleConfig.CronExpressions.Count > 0
            ? "Based on configured schedule"
            : "No schedules configured";
    }

    [RelayCommand]
    private void OpenManager()
    {
        // MVP stub — would switch MainViewModel to manager mode
    }
}
