using System.Windows;
using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;
using CdcExtractor.App.Views.Wizard;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App;

public partial class MainWindow : Window
{
    private readonly WizardViewModel _wizard;

    // Maps step index to the View type to resolve from DI
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

    public MainWindow(WizardViewModel wizard)
    {
        _wizard = wizard;
        InitializeComponent();

        // Wire the stepper
        Stepper.Steps = WizardViewModel.StepNames;
        Stepper.CurrentStep = _wizard.CurrentStep;

        // React to step changes
        _wizard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WizardViewModel.CurrentStep))
            {
                Stepper.CurrentStep = _wizard.CurrentStep;
                NavigateToCurrentStep();
            }
        };

        // Show the first page
        NavigateToCurrentStep();
    }

    private void NavigateToCurrentStep()
    {
        var step = _wizard.CurrentStep;
        if (step < 0 || step >= WizardPages.Length) return;

        var page = (UserControl)App.Services.GetRequiredService(WizardPages[step]);
        MainContent.Content = page;
    }
}
