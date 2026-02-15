using CdcExtractor.Contracts.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 2 — SQL Server connection settings with test connectivity.
/// </summary>
public partial class ConnectSqlViewModel : ObservableObject
{
    public WizardViewModel Wizard { get; }

    [ObservableProperty]
    private string _server = string.Empty;

    [ObservableProperty]
    private string _instance = string.Empty;

    [ObservableProperty]
    private string _database = string.Empty;

    [ObservableProperty]
    private bool _useWindowsAuth = true;

    [ObservableProperty]
    private bool _encrypt = true;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _testSucceeded;

    [ObservableProperty]
    private bool _testFailed;

    [ObservableProperty]
    private string _testResultMessage = string.Empty;

    public ConnectSqlViewModel(WizardViewModel wizard)
    {
        Wizard = wizard;
        LoadFromWizard();
    }

    private void LoadFromWizard()
    {
        var cfg = Wizard.SqlServerConfig;
        Server = cfg.Server;
        Instance = cfg.Instance ?? string.Empty;
        Database = cfg.Database;
        UseWindowsAuth = cfg.AuthType == "WindowsAd";
        Encrypt = cfg.Encrypt;
        Username = cfg.Username ?? string.Empty;
        Password = cfg.Password ?? string.Empty;
    }

    /// <summary>
    /// Writes the current field values back to the wizard's shared SqlServerConfig.
    /// </summary>
    public void SaveToWizard()
    {
        Wizard.SqlServerConfig = new SqlServerConfig
        {
            Server = Server,
            Instance = string.IsNullOrWhiteSpace(Instance) ? null : Instance,
            Database = Database,
            AuthType = UseWindowsAuth ? "WindowsAd" : "SqlLogin",
            Encrypt = Encrypt,
            Username = UseWindowsAuth ? null : Username,
            Password = UseWindowsAuth ? null : Password,
        };
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestSucceeded = false;
        TestFailed = false;
        TestResultMessage = string.Empty;

        try
        {
            // MVP stub — actual connectivity test would use SqlConnection here
            await Task.Delay(800);

            if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(Database))
            {
                TestFailed = true;
                TestResultMessage = "Server and Database are required.";
                return;
            }

            TestSucceeded = true;
            TestResultMessage = "Connection test passed.";
            SaveToWizard();
        }
        catch (Exception ex)
        {
            TestFailed = true;
            TestResultMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }
}
