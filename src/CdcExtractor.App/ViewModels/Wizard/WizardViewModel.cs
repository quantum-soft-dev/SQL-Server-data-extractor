using CdcExtractor.Contracts.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Shared wizard state across all 9 steps. Manages navigation, validation gating,
/// and configuration accumulation.
/// </summary>
public partial class WizardViewModel : ObservableObject
{
    public static readonly string[] StepNames =
    [
        "Welcome",
        "Connect SQL",
        "Downstream Auth",
        "Select Tables",
        "CDC Policy",
        "Schedule",
        "Review & Apply",
        "Bootstrap Run",
        "Done"
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _currentStep;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _isCurrentStepValid;

    [ObservableProperty]
    private bool _isApplying;

    // Wizard state — accumulated across steps
    [ObservableProperty]
    private SqlServerConfig _sqlServerConfig = new();

    [ObservableProperty]
    private DownstreamConfig _downstreamConfig = new();

    [ObservableProperty]
    private ScheduleConfig _scheduleConfig = new();

    [ObservableProperty]
    private CdcConfig _cdcConfig = new();

    [ObservableProperty]
    private ExtractionConfig _extractionConfig = new();

    [ObservableProperty]
    private List<string> _selectedTables = [];

    public int TotalSteps => StepNames.Length;

    public WizardViewModel()
    {
        CurrentStep = 0;
        IsCurrentStepValid = true;
    }

    public AppConfig BuildConfig() => new()
    {
        SqlServer = SqlServerConfig,
        Downstream = DownstreamConfig,
        Schedule = ScheduleConfig,
        Cdc = CdcConfig,
        Extraction = ExtractionConfig,
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (CurrentStep < TotalSteps - 1)
        {
            CurrentStep++;
            IsCurrentStepValid = true; // Reset for new step
        }
    }

    private bool CanGoNext() => CurrentStep < TotalSteps - 1 && IsCurrentStepValid;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
            IsCurrentStepValid = true;
        }
    }

    private bool CanGoBack() => CurrentStep > 0;

    [RelayCommand]
    private void Cancel()
    {
        // Reset wizard state
        CurrentStep = 0;
        SqlServerConfig = new();
        DownstreamConfig = new();
        ScheduleConfig = new();
        CdcConfig = new();
        ExtractionConfig = new();
        SelectedTables = [];
    }
}
