using CdcExtractor.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly NavigationService _navigationService;

    [ObservableProperty]
    private bool _isWizardMode;

    [ObservableProperty]
    private string _title = "SQL Server CDC Data Extractor";

    public MainViewModel(ConfigService configService, NavigationService navigationService)
    {
        _configService = configService;
        _navigationService = navigationService;

        // Detect mode based on existing config
        IsWizardMode = !_configService.ConfigExists();
    }

    [RelayCommand]
    private void SwitchToWizard()
    {
        IsWizardMode = true;
    }

    [RelayCommand]
    private void SwitchToManager()
    {
        IsWizardMode = false;
    }
}
