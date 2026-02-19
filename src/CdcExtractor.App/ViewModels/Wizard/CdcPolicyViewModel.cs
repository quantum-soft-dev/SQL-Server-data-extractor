using CdcExtractor.Contracts.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 5 — CDC policy and extraction settings.
/// </summary>
public partial class CdcPolicyViewModel : ObservableObject
{
    public WizardViewModel Wizard { get; }

    [ObservableProperty]
    private bool _autoEnableDatabase = true;

    [ObservableProperty]
    private bool _autoEnableTables = true;

    [ObservableProperty]
    private int _retentionMinDays = 7;

    [ObservableProperty]
    private int _batchInactivityTtlMinutes = 10;

    [ObservableProperty]
    private int _maxBytesPerChunkMb = 10;

    [ObservableProperty]
    private int _smallTableSnapThreshold = 1000;

    public CdcPolicyViewModel(WizardViewModel wizard)
    {
        Wizard = wizard;
        LoadFromWizard();
    }

    private void LoadFromWizard()
    {
        var cdc = Wizard.CdcConfig;
        AutoEnableDatabase = cdc.AutoEnableDatabase;
        AutoEnableTables = cdc.AutoEnableTables;
        RetentionMinDays = cdc.RetentionMinDays;
        BatchInactivityTtlMinutes = cdc.BatchInactivityTtlMinutes;

        var ext = Wizard.ExtractionConfig;
        MaxBytesPerChunkMb = ext.MaxBytesPerChunk / (1024 * 1024);
        SmallTableSnapThreshold = ext.SmallTableSnapThreshold;
    }

    /// <summary>
    /// Writes the current field values back to the wizard's shared config objects.
    /// </summary>
    public void SaveToWizard()
    {
        Wizard.CdcConfig = new CdcConfig
        {
            AutoEnableDatabase = AutoEnableDatabase,
            AutoEnableTables = AutoEnableTables,
            RetentionMinDays = RetentionMinDays,
            BatchInactivityTtlMinutes = BatchInactivityTtlMinutes,
        };

        Wizard.ExtractionConfig = new ExtractionConfig
        {
            MaxBytesPerChunk = MaxBytesPerChunkMb * 1024 * 1024,
            SmallTableSnapThreshold = SmallTableSnapThreshold,
        };
    }
}
