using CdcExtractor.Contracts.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 3 — Downstream API authentication via device-code OAuth flow.
/// </summary>
public partial class DownstreamAuthViewModel : ObservableObject
{
    public WizardViewModel Wizard { get; }

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private bool _isAuthorizing;

    [ObservableProperty]
    private bool _isAuthorized;

    [ObservableProperty]
    private string _userCode = string.Empty;

    [ObservableProperty]
    private string _verificationUri = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public DownstreamAuthViewModel(WizardViewModel wizard)
    {
        Wizard = wizard;
        LoadFromWizard();
    }

    private void LoadFromWizard()
    {
        var cfg = Wizard.DownstreamConfig;
        BaseUrl = cfg.BaseUrl;
        ClientId = cfg.ClientId;
    }

    /// <summary>
    /// Writes the current field values back to the wizard's shared DownstreamConfig.
    /// </summary>
    public void SaveToWizard()
    {
        Wizard.DownstreamConfig = new DownstreamConfig
        {
            BaseUrl = BaseUrl,
            ClientId = ClientId,
            HeartbeatIntervalSeconds = Wizard.DownstreamConfig.HeartbeatIntervalSeconds,
        };
    }

    [RelayCommand]
    private async Task AuthorizeAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(ClientId))
        {
            StatusMessage = "Base URL and Client ID are required.";
            return;
        }

        IsAuthorizing = true;
        IsAuthorized = false;
        StatusMessage = "Starting device authorization flow...";

        try
        {
            // MVP stub — real implementation would call OAuth device-code endpoint
            await Task.Delay(600);
            UserCode = "ABCD-1234";
            VerificationUri = $"{BaseUrl.TrimEnd('/')}/device";
            StatusMessage = "Enter the code at the verification URL to authorize.";

            // Simulate polling for authorization completion
            await Task.Delay(1500);
            IsAuthorized = true;
            StatusMessage = "Authorization successful.";
            SaveToWizard();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Authorization failed: {ex.Message}";
        }
        finally
        {
            IsAuthorizing = false;
        }
    }

    [RelayCommand]
    private void CopyUserCode()
    {
        if (!string.IsNullOrWhiteSpace(UserCode))
        {
            System.Windows.Clipboard.SetText(UserCode);
        }
    }
}
