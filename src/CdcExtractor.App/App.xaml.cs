using System.Windows;
using CdcExtractor.App.Services;
using CdcExtractor.App.ViewModels;
using CdcExtractor.App.ViewModels.Wizard;
using CdcExtractor.App.Views.Wizard;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Services
        services.AddSingleton<ConfigService>();
        services.AddSingleton<NavigationService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<WizardViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<ConnectSqlViewModel>();
        services.AddTransient<DownstreamAuthViewModel>();
        services.AddTransient<SelectTablesViewModel>();
        services.AddTransient<CdcPolicyViewModel>();
        services.AddTransient<ScheduleViewModel>();
        services.AddTransient<ReviewApplyViewModel>();
        services.AddTransient<BootstrapRunViewModel>();
        services.AddTransient<DoneViewModel>();

        // Views (wizard pages)
        services.AddTransient<WelcomePage>();
        services.AddTransient<ConnectSqlPage>();
        services.AddTransient<DownstreamAuthPage>();
        services.AddTransient<SelectTablesPage>();
        services.AddTransient<CdcPolicyPage>();
        services.AddTransient<SchedulePage>();
        services.AddTransient<ReviewApplyPage>();
        services.AddTransient<BootstrapRunPage>();
        services.AddTransient<DonePage>();

        // Main window
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
