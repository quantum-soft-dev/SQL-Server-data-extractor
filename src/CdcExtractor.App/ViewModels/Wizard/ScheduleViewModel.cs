using System.Collections.ObjectModel;
using CdcExtractor.Contracts.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 6 — Schedule configuration with cron expressions and timezone.
/// </summary>
public partial class ScheduleViewModel : ObservableObject
{
    public WizardViewModel Wizard { get; }

    public ObservableCollection<string> CronExpressions { get; } = [];

    [ObservableProperty]
    private string _newCronExpression = string.Empty;

    [ObservableProperty]
    private string? _selectedCronExpression;

    [ObservableProperty]
    private string _timezone = "UTC";

    [ObservableProperty]
    private bool _preventParallelRuns = true;

    public IReadOnlyList<string> AvailableTimezones { get; }

    public ScheduleViewModel(WizardViewModel wizard)
    {
        Wizard = wizard;
        AvailableTimezones = TimeZoneInfo.GetSystemTimeZones().Select(tz => tz.Id).ToList();
        LoadFromWizard();
    }

    private void LoadFromWizard()
    {
        var cfg = Wizard.ScheduleConfig;
        Timezone = cfg.Timezone;
        PreventParallelRuns = cfg.PreventParallelRuns;

        CronExpressions.Clear();
        foreach (var expr in cfg.CronExpressions)
        {
            CronExpressions.Add(expr);
        }
    }

    [RelayCommand]
    private void AddCronExpression()
    {
        var expr = NewCronExpression.Trim();
        if (!string.IsNullOrWhiteSpace(expr) && !CronExpressions.Contains(expr))
        {
            CronExpressions.Add(expr);
            NewCronExpression = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveCronExpression()
    {
        if (SelectedCronExpression is not null)
        {
            CronExpressions.Remove(SelectedCronExpression);
            SelectedCronExpression = null;
        }
    }

    /// <summary>
    /// Writes the current field values back to the wizard's shared ScheduleConfig.
    /// </summary>
    public void SaveToWizard()
    {
        Wizard.ScheduleConfig = new ScheduleConfig
        {
            CronExpressions = CronExpressions.ToList(),
            Timezone = Timezone,
            PreventParallelRuns = PreventParallelRuns,
        };
    }
}
