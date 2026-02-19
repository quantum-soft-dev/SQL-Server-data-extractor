using System.Windows;
using CdcExtractor.App.Services;
using CdcExtractor.App.ViewModels;
using CdcExtractor.App.ViewModels.Wizard;
using CdcExtractor.App.Views.Manager;
using CdcExtractor.App.Views.Wizard;
using CdcExtractor.Contracts.Ipc;
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

        // IPC client — singleton manages the named-pipe connection to the Service
        services.AddSingleton<IpcClient>();
        services.AddSingleton<IExtractorService>(sp =>
            sp.GetRequiredService<IpcClient>().GetProxy());

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
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<RunsViewModel>();
        services.AddTransient<RunDetailsViewModel>();
        services.AddTransient<TablesViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LogsViewModel>();

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

        // Views (manager pages)
        services.AddTransient<DashboardPage>();
        services.AddTransient<RunsPage>();
        services.AddTransient<RunDetailsPage>();
        services.AddTransient<TablesPage>();
        services.AddTransient<DiagnosticsPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<LogsPage>();

        // Main window
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
