using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 1 — Welcome page with environment checks (OS, .NET runtime).
/// </summary>
public partial class WelcomeViewModel : ObservableObject
{
    public WizardViewModel Wizard { get; }

    public string OsDescription { get; }
    public string RuntimeVersion { get; }
    public string MachineName { get; }
    public bool IsWindowsOs { get; }

    public WelcomeViewModel(WizardViewModel wizard)
    {
        Wizard = wizard;
        OsDescription = RuntimeInformation.OSDescription;
        RuntimeVersion = RuntimeInformation.FrameworkDescription;
        MachineName = Environment.MachineName;
        IsWindowsOs = OperatingSystem.IsWindows();
    }
}
