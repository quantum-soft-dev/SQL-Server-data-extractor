using System.Collections.ObjectModel;
using CdcExtractor.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels;

/// <summary>
/// ViewModel for the Manager Logs page.
/// Loads recent log entries, supports subscribe/unsubscribe for live streaming,
/// copy, and export functionality.
/// </summary>
public partial class LogsViewModel : ObservableObject
{
    private readonly IExtractorService _service;

    [ObservableProperty]
    private string _minLevel = "Information";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isAutoScroll = true;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isSubscribed;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<LogEntryDto> LogEntries { get; } = [];

    public IReadOnlyList<string> LogLevels { get; } = ["Debug", "Information", "Warning", "Error"];

    public LogsViewModel(IExtractorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    [RelayCommand]
    private async Task LoadLogsAsync(CancellationToken ct = default)
    {
        try
        {
            ErrorMessage = null;
            var result = await _service.GetRecentLogsAsync(200, MinLevel, ct).ConfigureAwait(false);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Clear();
                foreach (var entry in result.Entries)
                {
                    LogEntries.Add(entry);
                }
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SubscribeAsync(CancellationToken ct = default)
    {
        try
        {
            ErrorMessage = null;
            var result = await _service.SubscribeLogsAsync(MinLevel, ct).ConfigureAwait(false);
            IsSubscribed = result.Subscribed;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UnsubscribeAsync(CancellationToken ct = default)
    {
        try
        {
            ErrorMessage = null;
            var result = await _service.UnsubscribeLogsAsync(ct).ConfigureAwait(false);
            if (result.Unsubscribed)
            {
                IsSubscribed = false;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void Copy()
    {
        var text = BuildLogText();
        if (!string.IsNullOrEmpty(text))
        {
            System.Windows.Clipboard.SetText(text);
        }
    }

    [RelayCommand]
    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = $"log-export-{DateTime.Now:yyyyMMdd-HHmmss}",
        };

        if (dialog.ShowDialog() == true)
        {
            var text = BuildLogText();
            System.IO.File.WriteAllText(dialog.FileName, text);
        }
    }

    private string BuildLogText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var entry in LogEntries)
        {
            sb.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}");
        }

        return sb.ToString();
    }
}
