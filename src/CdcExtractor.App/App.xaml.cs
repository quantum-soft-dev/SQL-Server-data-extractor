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

        // Workaround for .NET 9+/10 WPF regression (dotnet/wpf#10020, #10042):
        // DynamicResource optimization causes theme styles to resolve property values
        // to DependencyProperty.UnsetValue during initial layout of custom templates.
        // These errors are transient — the UI renders correctly after swallowing them.
        DispatcherUnhandledException += (_, args) =>
        {
            if (args.Exception.Message.Contains("DependencyProperty.UnsetValue") ||
                (args.Exception.InnerException?.Message.Contains("DependencyProperty.UnsetValue") ?? false))
            {
                args.Handled = true;
            }
        };

        var services = new ServiceCollection();

        // Services
        services.AddSingleton<ConfigService>();
        services.AddSingleton<NavigationService>();

        // IPC client — singleton manages the named-pipe connection to the Service
        services.AddSingleton<IpcClient>();
        services.AddSingleton<IExtractorService>(sp =>
        {
            var client = sp.GetRequiredService<IpcClient>();
            return client.IsConnected
                ? client.GetProxy()
                : new DisconnectedExtractorService();
        });

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
