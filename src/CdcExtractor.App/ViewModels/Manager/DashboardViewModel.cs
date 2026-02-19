using System.Collections.ObjectModel;
using CdcExtractor.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels;

/// <summary>
/// ViewModel for the Manager Dashboard page.
/// Displays service status, recent batches, live progress, and log entries.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly IExtractorService _service;

    [ObservableProperty]
    private bool _isServiceRunning;

    [ObservableProperty]
    private string _serviceUptime = string.Empty;

    [ObservableProperty]
    private int _servicePid;

    [ObservableProperty]
    private string? _currentBatchId;

    [ObservableProperty]
    private string? _currentBatchType;

    [ObservableProperty]
    private string? _currentBatchTrigger;

    [ObservableProperty]
    private DateTimeOffset? _nextScheduledRun;

    [ObservableProperty]
    private TimeSpan? _lastBatchDuration;

    [ObservableProperty]
    private TimeSpan _cdcLag;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<BatchSummaryDto> RecentBatches { get; } = [];

    public DashboardViewModel(IExtractorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    [RelayCommand]
    private async Task LoadStatusAsync(CancellationToken ct = default)
    {
        try
        {
            ErrorMessage = null;
            var status = await _service.GetStatusAsync(ct).ConfigureAwait(false);

            IsServiceRunning = status.IsRunning;
            ServiceUptime = status.ServiceUptime;
            ServicePid = status.ServicePid;
            CurrentBatchId = status.CurrentBatchId;
            CurrentBatchType = status.CurrentBatchType;
            CurrentBatchTrigger = status.CurrentBatchTrigger;
            NextScheduledRun = status.NextScheduledRun;

            if (status.CurrentBatchStartedAt.HasValue)
            {
                CdcLag = DateTimeOffset.UtcNow - status.CurrentBatchStartedAt.Value;
            }
        }
        catch (Exception ex)
        {
            IsServiceRunning = false;
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadRecentBatchesAsync(CancellationToken ct = default)
    {
        var result = await _service.GetRecentBatchesAsync(10, ct).ConfigureAwait(false);

        RecentBatches.Clear();
        foreach (var batch in result.Batches)
        {
            RecentBatches.Add(batch);
        }

        if (result.Batches.Count > 0)
        {
            var last = result.Batches[0];
            if (last.FinishedAt.HasValue)
            {
                LastBatchDuration = last.FinishedAt.Value - last.StartedAt;
            }
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        await LoadStatusAsync(ct).ConfigureAwait(false);
        await LoadRecentBatchesAsync(ct).ConfigureAwait(false);
    }
}
