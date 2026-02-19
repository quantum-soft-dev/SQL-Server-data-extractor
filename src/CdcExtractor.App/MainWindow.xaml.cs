using System.Windows;
using System.Windows.Controls;
using CdcExtractor.App.Services;
using CdcExtractor.App.ViewModels;
using CdcExtractor.App.ViewModels.Wizard;
using CdcExtractor.App.Views.Manager;
using CdcExtractor.App.Views.Wizard;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainVm;
    private readonly WizardViewModel _wizard;
    private readonly NavigationService _navigationService;

    private static readonly Type[] WizardPages =
    [
        typeof(WelcomePage),
        typeof(ConnectSqlPage),
        typeof(DownstreamAuthPage),
        typeof(SelectTablesPage),
        typeof(CdcPolicyPage),
        typeof(SchedulePage),
        typeof(ReviewApplyPage),
        typeof(BootstrapRunPage),
        typeof(DonePage),
    ];

    private static readonly Dictionary<string, Type> ManagerPages = new()
    {
        ["Dashboard"] = typeof(DashboardPage),
        ["Runs"] = typeof(RunsPage),
        ["Tables"] = typeof(TablesPage),
        ["Diagnostics"] = typeof(DiagnosticsPage),
        ["Settings"] = typeof(SettingsPage),
        ["Logs"] = typeof(LogsPage),
    };

    public MainWindow(MainViewModel mainVm, WizardViewModel wizard, NavigationService navigationService)
    {
        _mainVm = mainVm;
        _wizard = wizard;
        _navigationService = navigationService;

        InitializeComponent();

        // Wire the wizard stepper
        Stepper.Steps = WizardViewModel.StepNames;
        Stepper.CurrentStep = _wizard.CurrentStep;

        _wizard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WizardViewModel.CurrentStep))
            {
                Stepper.CurrentStep = _wizard.CurrentStep;
                NavigateToCurrentWizardStep();
            }
        };

        // React to mode changes
        _mainVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsWizardMode))
            {
                ApplyMode();
            }
        };

        // Handle NavigationService requests (e.g. RunsViewModel → RunDetailsPage)
        _navigationService.NavigationRequested += pageType =>
        {
            if (!_mainVm.IsWizardMode)
            {
                var page = (UserControl)App.Services.GetRequiredService(pageType);
                ManagerContent.Content = page;
            }
        };

        // Apply initial mode
        ApplyMode();
    }

    private void ApplyMode()
    {
        if (_mainVm.IsWizardMode)
        {
            WizardPanel.Visibility = Visibility.Visible;
            ManagerPanel.Visibility = Visibility.Collapsed;
            NavigateToCurrentWizardStep();
        }
        else
        {
            WizardPanel.Visibility = Visibility.Collapsed;
            ManagerPanel.Visibility = Visibility.Visible;
            ConnectAndShowDashboard();
        }
    }

    private async void ConnectAndShowDashboard()
    {
        try
        {
            var ipcClient = App.Services.GetRequiredService<IpcClient>();
            if (!ipcClient.IsConnected)
            {
                await ipcClient.ConnectAsync();
            }
        }
        catch
        {
            // Service not running — Manager pages will show connection errors
        }

        NavigateToManagerPage("Dashboard");
    }

    private void NavigateToCurrentWizardStep()
    {
        var step = _wizard.CurrentStep;
        if (step < 0 || step >= WizardPages.Length) return;

        var page = (UserControl)App.Services.GetRequiredService(WizardPages[step]);
        WizardContent.Content = page;
    }

    private void NavigateToManagerPage(string pageName)
    {
        if (ManagerPages.TryGetValue(pageName, out var pageType))
        {
            var page = (UserControl)App.Services.GetRequiredService(pageType);
            ManagerContent.Content = page;
        }
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is ListBoxItem item && item.Tag is string pageName)
        {
            NavigateToManagerPage(pageName);
        }
    }

    private void OnRerunWizardClick(object sender, RoutedEventArgs e)
    {
        _mainVm.SwitchToWizardCommand.Execute(null);
    }
}
