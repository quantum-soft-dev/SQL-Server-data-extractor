using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 8 — Bootstrap initial snapshot run with per-table progress and live log.
/// </summary>
public partial class BootstrapRunViewModel : ObservableObject
{
    public WizardViewModel Wizard { get; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _hasFailed;

    [ObservableProperty]
    private string _statusMessage = "Ready to start bootstrap run.";

    public ObservableCollection<BootstrapTableProgress> TableProgress { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];

    public BootstrapRunViewModel(WizardViewModel wizard)
    {
        Wizard = wizard;
    }

    /// <summary>
    /// Initialise progress items from selected tables.
    /// </summary>
    public void Prepare()
    {
        TableProgress.Clear();
        LogEntries.Clear();
        IsComplete = false;
        HasFailed = false;
        StatusMessage = "Ready to start bootstrap run.";

        foreach (var table in Wizard.SelectedTables)
        {
            TableProgress.Add(new BootstrapTableProgress(table));
        }
    }

    [RelayCommand]
    private async Task RunBootstrapAsync(CancellationToken ct)
    {
        IsRunning = true;
        StatusMessage = "Running bootstrap...";
        AddLog("Bootstrap run started.");

        try
        {
            // MVP stub — simulate per-table extraction
            foreach (var tp in TableProgress)
            {
                ct.ThrowIfCancellationRequested();
                tp.Status = "Running";
                AddLog($"Processing {tp.TableName}...");

                for (var pct = 0; pct <= 100; pct += 20)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(200, ct);
                    tp.ProgressPercent = pct;
                }

                tp.Status = "Done";
                AddLog($"Completed {tp.TableName}.");
            }

            IsComplete = true;
            StatusMessage = "Bootstrap run completed successfully.";
            AddLog("All tables processed.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Bootstrap run was cancelled.";
            AddLog("Run cancelled by user.");
        }
        catch (Exception ex)
        {
            HasFailed = true;
            StatusMessage = $"Bootstrap failed: {ex.Message}";
            AddLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void AddLog(string message)
    {
        LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}

/// <summary>
/// Tracks progress for a single table during the bootstrap run.
/// </summary>
public partial class BootstrapTableProgress : ObservableObject
{
    public string TableName { get; }

    [ObservableProperty]
    private string _status = "Pending";

    [ObservableProperty]
    private int _progressPercent;

    public BootstrapTableProgress(string tableName)
    {
        TableName = tableName;
    }
}
